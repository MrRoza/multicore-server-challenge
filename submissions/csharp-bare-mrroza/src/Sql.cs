using System.Text;

namespace Bare;

/// Every statement. Reads take arrays of a batch's distinct keys. Rows looked up by id start with the id: one scan with
/// = ANY costs far less than a nested loop restarting a scan per key, and a batch's ids often share index pages. Rows
/// looked up by text start with the 1-based position of their key (WITH ORDINALITY), so they find their waiting requests
/// without comparing strings.
///
/// Every slug ends in its article's id (SLUGIFY(title) || '-' || id, in the seed as in every write), so articles are found
/// by primary key: an integer comparison per level instead of text comparisons in the slug index. The row's slug is then
/// compared with the one asked for.
static class Sql
{
    public const int UsersById = 0, UsersByEmail = 1, Profiles = 2, ArticleFull = 3, SlugOnly = 4, Comments = 5,
        PageGlobal = 6, PageTag = 7, PageAuthor = 8, PageFavorited = 9, PageFeed = 10,
        CountGlobal = 11, CountTag = 12, CountAuthor = 13, CountFavorited = 14,
        ArticleRows = 15, ArticleTags = 16, FavoriteCounts = 17, Favorited = 18, Tags = 19,
        Authors = 20, Following = 21,
        Followees = 22, FeedHubs = 23, FeedOthers = 24, CountFeed = 25;

    public static readonly string[] Read =
    {
        // ----- round A: what the request names -----
        @"SELECT u.id, u.username, u.email, u.bio, u.image FROM users u WHERE u.id = ANY($1::int8[])",
        @"SELECT p.ord, u.id, u.username, u.email, u.bio, u.image, u.password_hash
            FROM unnest($1::text[]) WITH ORDINALITY p(e, ord) JOIN users u ON u.email = p.e",
        @"SELECT p.ord, u.id, u.username, u.bio, u.image
            FROM unnest($1::text[]) WITH ORDINALITY p(n, ord) JOIN users u ON u.username = p.n",
        @"SELECT a.id, a.author_id, a.slug, a.title, a.description, a.created_at, a.updated_at, a.body
            FROM articles a WHERE a.id = ANY($1::int8[])",
        @"SELECT a.id, a.slug FROM articles a WHERE a.id = ANY($1::int8[])",
        // A nested loop, the only join these sessions allow, keeps each article's comments together and in order.
        @"SELECT p.ord, c.id, c.body, c.created_at, c.updated_at, c.author_id
            FROM unnest($1::int8[]) WITH ORDINALITY p(id, ord)
            CROSS JOIN LATERAL (SELECT * FROM comments c WHERE c.article_id = p.id
                                 ORDER BY c.created_at DESC, c.id DESC LIMIT 100) c",

        // Pages are arrays of ids, cut from an index before anything is joined.
        @"SELECT p.ord, ARRAY(SELECT a.id FROM articles a ORDER BY a.id DESC LIMIT p.l OFFSET p.o)
            FROM unnest($1::int4[], $2::int4[]) WITH ORDINALITY p(l, o, ord)",
        @"SELECT p.ord, ARRAY(SELECT at.article_id FROM article_tags at
                               WHERE at.tag_id = (SELECT t.id FROM tags t WHERE t.name = p.k)
                               ORDER BY at.article_id DESC LIMIT p.l OFFSET p.o)
            FROM unnest($1::text[], $2::int4[], $3::int4[]) WITH ORDINALITY p(k, l, o, ord)",
        @"SELECT p.ord, ARRAY(SELECT a.id FROM articles a
                               WHERE a.author_id = (SELECT u.id FROM users u WHERE u.username = p.k)
                               ORDER BY a.id DESC LIMIT p.l OFFSET p.o)
            FROM unnest($1::text[], $2::int4[], $3::int4[]) WITH ORDINALITY p(k, l, o, ord)",
        @"SELECT p.ord, ARRAY(SELECT f.article_id FROM favorites f
                               WHERE f.user_id = (SELECT u.id FROM users u WHERE u.username = p.k)
                               ORDER BY f.article_id DESC LIMIT p.l OFFSET p.o)
            FROM unnest($1::text[], $2::int4[], $3::int4[]) WITH ORDINALITY p(k, l, o, ord)",
        // A feed the planner in ReadBatch has no floor for: the reference statement, per viewer.
        @"SELECT p.ord, ARRAY(SELECT y.id FROM follows fo CROSS JOIN LATERAL
                                 (SELECT a.id FROM articles a WHERE a.author_id = fo.followee_id
                                   ORDER BY a.id DESC LIMIT p.l::int8 + p.o::int8) y
                                WHERE fo.follower_id = p.v ORDER BY y.id DESC LIMIT p.l OFFSET p.o)
            FROM unnest($1::int8[], $2::int4[], $3::int4[]) WITH ORDINALITY p(v, l, o, ord)",

        // Capped counts. The global one walks the oldest end of the key, where pages are all-visible. A tag nearly
        // always has 1,000 articles, and skipping them costs less than counting them.
        @"SELECT 1::int8, count(*) FROM (SELECT 1 FROM articles a ORDER BY a.id LIMIT 1000) s",
        @"SELECT p.ord, CASE WHEN EXISTS (SELECT 1 FROM article_tags at WHERE at.tag_id = t.id ORDER BY at.article_id LIMIT 1 OFFSET 999) THEN 1000::int8
                             ELSE (SELECT count(*) FROM article_tags at WHERE at.tag_id = t.id) END
            FROM unnest($1::text[]) WITH ORDINALITY p(k, ord) JOIN tags t ON t.name = p.k",
        @"SELECT p.ord, (SELECT count(*) FROM (SELECT 1 FROM articles a
                          WHERE a.author_id = (SELECT u.id FROM users u WHERE u.username = p.k) LIMIT 1000) s)
            FROM unnest($1::text[]) WITH ORDINALITY p(k, ord)",
        @"SELECT p.ord, (SELECT count(*) FROM (SELECT 1 FROM favorites f
                          WHERE f.user_id = (SELECT u.id FROM users u WHERE u.username = p.k) LIMIT 1000) s)
            FROM unnest($1::text[]) WITH ORDINALITY p(k, ord)",

        // ----- round B: each article once. Tag names come from one read of the tags table, last, so it has every tag
        // the articles above it can name. -----
        @"SELECT a.id, a.author_id, a.slug, a.title, a.description, a.created_at, a.updated_at
            FROM articles a WHERE a.id = ANY($1::int8[])",
        // An article with no tags or favorites has no row here.
        @"SELECT at.article_id, at.tag_id FROM article_tags at WHERE at.article_id = ANY($1::int8[])",
        @"SELECT f.article_id, count(*) FROM favorites f WHERE f.article_id = ANY($1::int8[]) GROUP BY f.article_id",
        // One primary-key scan per viewer over that viewer's articles, not one descent per pair. Only hits come back.
        @"SELECT p.v, f.article_id FROM unnest($1::int8[], $3::int4[], $4::int4[]) p(v, s, e)
            CROSS JOIN LATERAL (SELECT f.article_id FROM favorites f
                                 WHERE f.user_id = p.v AND f.article_id = ANY(($2::int8[])[p.s : p.e])) f",
        @"SELECT t.id, t.name FROM tags t ORDER BY t.name",

        // ----- round C: each author once, and (viewer, author) pairs, once the authors are known -----
        @"SELECT u.id, u.username, u.bio, u.image FROM users u WHERE u.id = ANY($1::int8[])",
        @"SELECT p.v, f.followee_id FROM unnest($1::int8[], $3::int4[], $4::int4[]) p(v, s, e)
            CROSS JOIN LATERAL (SELECT f.followee_id FROM follows f
                                 WHERE f.follower_id = p.v AND f.followee_id = ANY(($2::int8[])[p.s : p.e])) f",

        // ----- feeds, between rounds A and B. Each viewer's followees; then the followees most viewers of the batch share,
        // with their newest ids, and the first few checked for 1,000 articles, which settles the count of everyone
        // following them; then all other followees at once, down to the lowest floor those ids gave any viewer. -----
        @"SELECT fo.follower_id, fo.followee_id FROM follows fo WHERE fo.follower_id = ANY($1::int8[])",
        @"SELECT p.id, ARRAY(SELECT a.id FROM articles a WHERE a.author_id = p.id ORDER BY a.id DESC LIMIT p.d),
                 CASE WHEN p.big = 1 THEN EXISTS (SELECT 1 FROM articles a WHERE a.author_id = p.id ORDER BY a.id DESC LIMIT 1 OFFSET 999) ELSE false END
            FROM unnest($1::int8[], $2::int4[], $3::int4[]) p(id, d, big)",
        @"SELECT a.author_id, a.id FROM articles a WHERE a.author_id = ANY($1::int8[]) AND a.id >= $2::int8",
        @"SELECT p.ord, (SELECT count(*) FROM (SELECT 1 FROM articles a
                          WHERE a.author_id IN (SELECT fo.followee_id FROM follows fo WHERE fo.follower_id = p.v) LIMIT 1000) s)
            FROM unnest($1::int8[]) WITH ORDINALITY p(v, ord)",
    };

    public const int Register = 0, UpdateUser = 1, Follow = 2, Unfollow = 3, Favorite = 4, Unfavorite = 5,
        CreateComment = 6, DeleteComment = 7, InsertTags = 8, CreateArticle = 9, UpdateArticle = 10, DeleteArticle = 11;

    const string Slugify = "trim(both '-' from lower(regexp_replace({0}, '[^A-Za-z0-9]+', '-', 'g')))";

    /// Each write is one statement that reports "not found" or "not yours" as data, never as an error, so a normal
    /// outcome can't abort the group commit it shares. "(slug = x) IS TRUE" can't use the slug index, which keeps the
    /// lookup on the primary key.
    public static readonly string[] Write =
    {
        @"INSERT INTO users (username, email, password_hash) VALUES ($1::text, $2::text, $3::text)
          ON CONFLICT DO NOTHING RETURNING id, username, email, bio, image",
        @"UPDATE users SET email = COALESCE($2::text, email), username = COALESCE($3::text, username),
                 password_hash = COALESCE($4::text, password_hash), bio = COALESCE($5::text, bio), image = COALESCE($6::text, image)
           WHERE id = $1::int8 RETURNING id, username, email, bio, image",
        @"WITH t AS (SELECT id, username, bio, image FROM users WHERE username = $2::text),
               i AS (INSERT INTO follows (follower_id, followee_id) SELECT $1::int8, t.id FROM t ON CONFLICT DO NOTHING)
          SELECT id, username, bio, image FROM t",
        @"WITH t AS (SELECT id, username, bio, image FROM users WHERE username = $2::text),
               d AS (DELETE FROM follows f USING t WHERE f.follower_id = $1::int8 AND f.followee_id = t.id)
          SELECT id, username, bio, image FROM t",
        @"WITH t AS (SELECT id FROM articles WHERE id = $2::int8 AND (slug = $3::text) IS TRUE),
               i AS (INSERT INTO favorites (user_id, article_id) SELECT $1::int8, t.id FROM t ON CONFLICT DO NOTHING)
          SELECT id FROM t",
        @"WITH t AS (SELECT id FROM articles WHERE id = $2::int8 AND (slug = $3::text) IS TRUE),
               d AS (DELETE FROM favorites f USING t WHERE f.user_id = $1::int8 AND f.article_id = t.id)
          SELECT id FROM t",
        @"WITH t AS (SELECT id FROM articles WHERE id = $2::int8 AND (slug = $3::text) IS TRUE),
               c AS (INSERT INTO comments (article_id, author_id, body) SELECT t.id, $1::int8, $4::text FROM t
                     RETURNING id, body, created_at, updated_at, author_id)
          SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image
            FROM c JOIN users u ON u.id = c.author_id",
        @"WITH t AS (SELECT c.id, c.author_id FROM comments c JOIN articles a ON a.id = c.article_id
                      WHERE c.id = $1::int8 AND a.id = $2::int8 AND (a.slug = $3::text) IS TRUE),
               d AS (DELETE FROM comments x USING t WHERE x.id = t.id AND t.author_id = $4::int8)
          SELECT author_id FROM t",
        // Its own statement, so the next one's snapshot sees tags another transaction just committed.
        @"INSERT INTO tags (name) SELECT DISTINCT unnest($1::text[]) ON CONFLICT (name) DO NOTHING",
        @"WITH n AS (SELECT nextval(pg_get_serial_sequence('articles', 'id')) AS id),
               a AS (INSERT INTO articles (id, slug, author_id, title, description, body)
                     SELECT n.id, " + string.Format(Slugify, "$2::text") + @" || '-' || n.id, $1::int8, $2::text, $3::text, $4::text FROM n
                     RETURNING id),
               x AS (INSERT INTO article_tags (article_id, tag_id)
                     SELECT a.id, t.id FROM a, tags t WHERE t.name = ANY($5::text[]))
          SELECT id FROM a",
        @"WITH t AS (SELECT id, author_id FROM articles WHERE id = $1::int8 AND (slug = $2::text) IS TRUE),
               u AS (UPDATE articles a SET title = COALESCE($4::text, a.title), description = COALESCE($5::text, a.description),
                            body = COALESCE($6::text, a.body),
                            slug = CASE WHEN $4::text IS NULL THEN a.slug ELSE " + string.Format(Slugify, "$4::text") + @" || '-' || a.id END,
                            updated_at = now()
                       FROM t WHERE a.id = t.id AND t.author_id = $3::int8)
          SELECT id, author_id FROM t",
        @"WITH t AS (SELECT id, author_id FROM articles WHERE id = $1::int8 AND (slug = $2::text) IS TRUE),
               d AS (DELETE FROM articles a USING t WHERE a.id = t.id AND t.author_id = $3::int8)
          SELECT id, author_id FROM t",
    };

    public static readonly byte[][] ReadName = Names("r", Read.Length);
    public static readonly byte[][] WriteName = Names("w", Write.Length);

    static byte[][] Names(string prefix, int n)
    {
        var a = new byte[n][];
        for (int i = 0; i < n; i++) a[i] = Encoding.ASCII.GetBytes(prefix + i + "\0");
        return a;
    }
}
