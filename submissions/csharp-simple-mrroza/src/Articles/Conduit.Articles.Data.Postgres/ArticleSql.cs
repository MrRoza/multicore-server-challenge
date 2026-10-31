namespace Conduit.Articles.Data.Postgres;

/// <summary>The article reads from SPEC.md.</summary>
public static class ArticleSql
{
    const string Fields = """
        ARRAY(SELECT t.name FROM article_tags at JOIN tags t ON t.id = at.tag_id
               WHERE at.article_id = a.id ORDER BY t.name) AS tag_list,
        (SELECT count(*) FROM favorites f WHERE f.article_id = a.id) AS favorites_count,
        EXISTS (SELECT 1 FROM favorites f WHERE f.article_id = a.id AND f.user_id = @Viewer) AS favorited,
        u.username, u.bio, u.image,
        EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @Viewer AND fo.followee_id = a.author_id) AS following
        FROM articles a
        JOIN users u ON u.id = a.author_id
        """;

    const string ListSelect = "SELECT a.id, a.slug, a.title, a.description, a.created_at, a.updated_at, " + Fields;
    const string NewestPage = " ORDER BY a.id DESC LIMIT @Limit OFFSET @Offset";

    public const string FullSelect = "SELECT a.id, a.slug, a.title, a.description, a.body, a.created_at, a.updated_at, " + Fields;

    // Picks the page first, so the subqueries in Fields don't run for the rows OFFSET skips.
    public const string All = ListSelect + """

         WHERE a.id IN (SELECT a2.id FROM articles a2 ORDER BY a2.id DESC LIMIT @Limit OFFSET @Offset)
         ORDER BY a.id DESC
        """;
    public const string CountAll = "SELECT count(*) FROM (SELECT 1 FROM articles a LIMIT 1000) s";

    public const string ByTag = ListSelect + """

         WHERE a.id IN (SELECT at.article_id FROM article_tags at
                         WHERE at.tag_id = (SELECT t.id FROM tags t WHERE t.name = @filter)
                         ORDER BY at.article_id DESC LIMIT @Limit OFFSET @Offset)
         ORDER BY a.id DESC
        """;
    public const string CountByTag = """
        SELECT count(*) FROM (SELECT 1 FROM article_tags at
                               WHERE at.tag_id = (SELECT t.id FROM tags t WHERE t.name = @filter) LIMIT 1000) s
        """;

    public const string ByAuthor = ListSelect + " WHERE a.author_id = (SELECT au.id FROM users au WHERE au.username = @filter)" + NewestPage;
    public const string CountByAuthor = """
        SELECT count(*) FROM (SELECT 1 FROM articles a
                               WHERE a.author_id = (SELECT u.id FROM users u WHERE u.username = @filter) LIMIT 1000) s
        """;

    public const string ByFavorited = ListSelect + """

         WHERE a.id IN (SELECT f.article_id FROM favorites f
                         WHERE f.user_id = (SELECT fu.id FROM users fu WHERE fu.username = @filter)
                         ORDER BY f.article_id DESC LIMIT @Limit OFFSET @Offset)
         ORDER BY a.id DESC
        """;
    public const string CountByFavorited = """
        SELECT count(*) FROM (SELECT 1 FROM favorites f
                               WHERE f.user_id = (SELECT fu.id FROM users fu WHERE fu.username = @filter) LIMIT 1000) s
        """;

    public const string Feed = ListSelect + """

         WHERE a.id IN (SELECT x.id FROM follows fo CROSS JOIN LATERAL
                          (SELECT a2.id FROM articles a2 WHERE a2.author_id = fo.followee_id
                            ORDER BY a2.id DESC LIMIT (@Limit::bigint + @Offset::bigint)) x
                         WHERE fo.follower_id = @Viewer ORDER BY x.id DESC LIMIT @Limit OFFSET @Offset)
         ORDER BY a.id DESC
        """;
    public const string CountFeed = """
        SELECT count(*) FROM (SELECT 1 FROM articles a
                               WHERE a.author_id IN (SELECT fo.followee_id FROM follows fo WHERE fo.follower_id = @Viewer) LIMIT 1000) s
        """;

    public static string Slugify(string parameter) =>
        $"trim(both '-' from lower(regexp_replace({parameter}, '[^A-Za-z0-9]+', '-', 'g')))";
}
