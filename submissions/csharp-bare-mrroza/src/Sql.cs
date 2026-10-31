using System.Text;

namespace Bare;

/// Every statement. Reads take arrays of a batch's distinct keys, and each row starts with the 1-based position of
/// its key (WITH ORDINALITY), so a row finds its waiting requests without comparing strings.
static class Sql
{
    public const int UsersById = 0, UsersByEmail = 1, Profiles = 2, Slugs = 3,
        PageGlobal = 4, PageTag = 5, PageAuthor = 6, PageFavorited = 7, PageFeed = 8,
        CountGlobal = 9, CountTag = 10, CountAuthor = 11, CountFavorited = 12, CountFeed = 13,
        Info = 14, Bodies = 15, Favorited = 16, Comments = 17, Tags = 18, Following = 19;

    public static readonly string[] Read =
    {
        // ----- phase A: what the request names -----
        @"SELECT p.ord, u.username, u.email, u.bio, u.image
            FROM unnest($1::int8[]) WITH ORDINALITY p(id, ord) JOIN users u ON u.id = p.id",
        @"SELECT p.ord, u.id, u.username, u.email, u.bio, u.image, u.password_hash
            FROM unnest($1::text[]) WITH ORDINALITY p(e, ord) JOIN users u ON u.email = p.e",
        @"SELECT p.ord, u.id, u.username, u.bio, u.image
            FROM unnest($1::text[]) WITH ORDINALITY p(n, ord) JOIN users u ON u.username = p.n",
        @"SELECT p.ord, a.id
            FROM unnest($1::text[]) WITH ORDINALITY p(s, ord) JOIN articles a ON a.slug = p.s",

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
        @"SELECT p.ord, ARRAY(SELECT x.id FROM follows fo CROSS JOIN LATERAL
                                (SELECT a.id FROM articles a WHERE a.author_id = fo.followee_id
                                  ORDER BY a.id DESC LIMIT p.l::int8 + p.o::int8) x
                               WHERE fo.follower_id = p.v
                               ORDER BY x.id DESC LIMIT p.l OFFSET p.o)
            FROM unnest($1::int8[], $2::int4[], $3::int4[]) WITH ORDINALITY p(v, l, o, ord)",

        // Capped counts. The global one walks the oldest end of the key, where pages are all-visible.
        @"SELECT 1::int8, count(*) FROM (SELECT 1 FROM articles a ORDER BY a.id LIMIT 1000) s",
        @"SELECT p.ord, (SELECT count(*) FROM (SELECT 1 FROM article_tags at
                          WHERE at.tag_id = (SELECT t.id FROM tags t WHERE t.name = p.k) LIMIT 1000) s)
            FROM unnest($1::text[]) WITH ORDINALITY p(k, ord)",
        @"SELECT p.ord, (SELECT count(*) FROM (SELECT 1 FROM articles a
                          WHERE a.author_id = (SELECT u.id FROM users u WHERE u.username = p.k) LIMIT 1000) s)
            FROM unnest($1::text[]) WITH ORDINALITY p(k, ord)",
        @"SELECT p.ord, (SELECT count(*) FROM (SELECT 1 FROM favorites f
                          WHERE f.user_id = (SELECT u.id FROM users u WHERE u.username = p.k) LIMIT 1000) s)
            FROM unnest($1::text[]) WITH ORDINALITY p(k, ord)",
        @"SELECT p.ord, (SELECT count(*) FROM (SELECT 1 FROM articles a
                          WHERE a.author_id IN (SELECT fo.followee_id FROM follows fo WHERE fo.follower_id = p.v) LIMIT 1000) s)
            FROM unnest($1::int8[]) WITH ORDINALITY p(v, ord)",

        // ----- phase B: each article once. Tag names come from one read of the tags table, last, so it has every
        // tag the articles above it can name. -----
        @"SELECT p.ord, a.author_id, a.slug, a.title, a.description, a.created_at, a.updated_at,
                 u.username, u.bio, u.image,
                 ARRAY(SELECT at.tag_id FROM article_tags at WHERE at.article_id = a.id),
                 (SELECT count(*) FROM favorites f WHERE f.article_id = a.id)
            FROM unnest($1::int8[]) WITH ORDINALITY p(id, ord)
            JOIN articles a ON a.id = p.id
            JOIN users u ON u.id = a.author_id",
        @"SELECT p.ord, a.body
            FROM unnest($1::int8[]) WITH ORDINALITY p(id, ord) JOIN articles a ON a.id = p.id",
        @"SELECT p.ord FROM unnest($1::int8[], $2::int8[]) WITH ORDINALITY p(v, a, ord)
           WHERE EXISTS (SELECT 1 FROM favorites f WHERE f.user_id = p.v AND f.article_id = p.a)",
        @"SELECT p.ord, c.id, c.body, c.created_at, c.updated_at, c.author_id, u.username, u.bio, u.image
            FROM unnest($1::int8[]) WITH ORDINALITY p(id, ord)
            CROSS JOIN LATERAL (SELECT * FROM comments c WHERE c.article_id = p.id
                                 ORDER BY c.created_at DESC, c.id DESC LIMIT 100) c
            JOIN users u ON u.id = c.author_id
           ORDER BY p.ord, c.created_at DESC, c.id DESC",
        @"SELECT t.id, t.name FROM tags t ORDER BY t.name",

        // ----- phase C: (viewer, author) pairs, once the authors are known -----
        @"SELECT p.ord FROM unnest($1::int8[], $2::int8[]) WITH ORDINALITY p(v, a, ord)
           WHERE EXISTS (SELECT 1 FROM follows f WHERE f.follower_id = p.v AND f.followee_id = p.a)",
    };

    public const int Register = 0, UpdateUser = 1, Follow = 2, Unfollow = 3, Favorite = 4, Unfavorite = 5,
        CreateComment = 6, DeleteComment = 7, InsertTags = 8, CreateArticle = 9, UpdateArticle = 10, DeleteArticle = 11;

    const string Slugify = "trim(both '-' from lower(regexp_replace({0}, '[^A-Za-z0-9]+', '-', 'g')))";

    /// Each write is one statement that reports "not found" or "not yours" as data, never as an error, so a normal
    /// outcome can't abort the group commit it shares.
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
        @"WITH t AS (SELECT id FROM articles WHERE slug = $2::text),
               i AS (INSERT INTO favorites (user_id, article_id) SELECT $1::int8, t.id FROM t ON CONFLICT DO NOTHING)
          SELECT id FROM t",
        @"WITH t AS (SELECT id FROM articles WHERE slug = $2::text),
               d AS (DELETE FROM favorites f USING t WHERE f.user_id = $1::int8 AND f.article_id = t.id)
          SELECT id FROM t",
        @"WITH t AS (SELECT id FROM articles WHERE slug = $2::text),
               c AS (INSERT INTO comments (article_id, author_id, body) SELECT t.id, $1::int8, $3::text FROM t
                     RETURNING id, body, created_at, updated_at, author_id)
          SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image
            FROM c JOIN users u ON u.id = c.author_id",
        @"WITH t AS (SELECT c.id, c.author_id FROM comments c JOIN articles a ON a.id = c.article_id
                      WHERE c.id = $1::int8 AND a.slug = $2::text),
               d AS (DELETE FROM comments x USING t WHERE x.id = t.id AND t.author_id = $3::int8)
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
        @"WITH t AS (SELECT id, author_id FROM articles WHERE slug = $1::text),
               u AS (UPDATE articles a SET title = COALESCE($3::text, a.title), description = COALESCE($4::text, a.description),
                            body = COALESCE($5::text, a.body),
                            slug = CASE WHEN $3::text IS NULL THEN a.slug ELSE " + string.Format(Slugify, "$3::text") + @" || '-' || a.id END,
                            updated_at = now()
                       FROM t WHERE a.id = t.id AND t.author_id = $2::int8)
          SELECT id, author_id FROM t",
        @"WITH t AS (SELECT id, author_id FROM articles WHERE slug = $1::text),
               d AS (DELETE FROM articles a USING t WHERE a.id = t.id AND t.author_id = $2::int8)
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
