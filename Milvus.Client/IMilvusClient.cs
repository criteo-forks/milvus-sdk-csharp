using System.Diagnostics.CodeAnalysis;

namespace Milvus.Client;

/// <summary>
/// Defines the public Milvus client API.
/// </summary>
[SuppressMessage("Error", "CA1716", Justification = "Will only use from C#")]
public interface IMilvusClient : IDisposable
{
    /// <summary>
    /// Base address of Milvus server.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Ensure to connect to Milvus server before any operations.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<MilvusHealthState> HealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Milvus version.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns an <see cref="IMilvusCollection" /> representing a Milvus collection in the default database. This is
    /// the starting point for all collection operations.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    public IMilvusCollection GetCollection(string collectionName);

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection to create.</param>
    /// <param name="fields">
    /// Schema of the fields within the collection to create. Refer to
    /// <see href="https://milvus.io/docs/schema.md" /> for more information.
    /// </param>
    /// <param name="consistencyLevel">
    /// The consistency level to be used by the collection. Defaults to <see cref="ConsistencyLevel.Session" />.
    /// </param>
    /// <param name="shardsNum">Number of the shards for the collection to create.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<IMilvusCollection> CreateCollectionAsync(
        string collectionName,
        IReadOnlyList<FieldSchema> fields,
        ConsistencyLevel consistencyLevel = ConsistencyLevel.Session,
        int shardsNum = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection to create.</param>
    /// <param name="schema">The schema definition for the collection.</param>
    /// <param name="consistencyLevel">
    /// The consistency level to be used by the collection. Defaults to <see cref="ConsistencyLevel.Session" />.
    /// </param>
    /// <param name="shardsNum">Number of the shards for the collection to create.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<IMilvusCollection> CreateCollectionAsync(
        string collectionName,
        CollectionSchema schema,
        ConsistencyLevel consistencyLevel = ConsistencyLevel.Session,
        int shardsNum = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a collection exists.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="timestamp">
    /// If non-zero, returns <c>true</c> only if the collection was created before the given timestamp.
    /// </param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<bool> HasCollectionAsync(
        string collectionName,
        ulong timestamp = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the collections available in the database.
    /// </summary>
    /// <param name="collectionNames">An optional list of collection names by which to filter.</param>
    /// <param name="filter">
    /// Determines whether all collections are returned, or only ones which have been loaded to memory.
    /// </param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<IReadOnlyList<MilvusCollectionInfo>> ListCollectionsAsync(
        IReadOnlyList<string>? collectionNames = null,
        CollectionFilter filter = CollectionFilter.All,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes collection data to disk, required only in order to get up-to-date statistics.
    /// </summary>
    /// <remarks>
    /// This method will be removed in a future version.
    /// </remarks>
    /// <param name="collectionNames">The names of the collections to be flushed.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<FlushResult> FlushAsync(
        IReadOnlyList<string> collectionNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an alias for a collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection for which to create the alias.</param>
    /// <param name="alias">The alias to be created.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task CreateAliasAsync(
        string collectionName,
        string alias,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops an alias.
    /// </summary>
    /// <param name="alias">The alias to be dropped.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task DropAliasAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alters an alias to point to a new collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection to which the alias should point.</param>
    /// <param name="alias">The alias to be altered.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task AlterAliasAsync(
        string collectionName,
        string alias,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes an alias and returns the name of the collection it points to.
    /// </summary>
    /// <param name="alias">The alias to describe.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>The name of the collection that the alias points to.</returns>
    public Task<string> DescribeAliasAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all aliases in the current database.
    /// </summary>
    /// <param name="collectionName">
    /// Optional collection name to filter aliases. If specified, only returns aliases for this collection.
    /// </param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A list of alias names.</returns>
    public Task<IList<string>> ListAliasesAsync(
        string? collectionName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the state of a compaction previously started via <see cref="MilvusCollection.CompactAsync" />.
    /// </summary>
    /// <param name="compactionId">The compaction ID returned by <see cref="MilvusCollection.CompactAsync" />.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<CompactionState> GetCompactionStateAsync(
        long compactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls Milvus for the state of a compaction process until it is complete..
    /// To perform a single progress check, use <see cref="GetCompactionStateAsync" />.
    /// </summary>
    /// <param name="compactionId">The compaction ID returned by <see cref="MilvusCollection.CompactAsync" />.</param>
    /// <param name="waitingInterval">Waiting interval. Defaults to 500 milliseconds.</param>
    /// <param name="timeout">How long to poll for before throwing a <see cref="TimeoutException" />.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task WaitForCompactionAsync(
        long compactionId,
        TimeSpan? waitingInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the compaction states of a compaction.
    /// </summary>
    /// <param name="compactionId">The compaction ID returned by <see cref="MilvusCollection.CompactAsync" />.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<CompactionPlans> GetCompactionPlansAsync(
        long compactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new database.
    /// </summary>
    /// <param name="databaseName">The name of the new database to be created.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// Available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    public Task CreateDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all available databases.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <returns>The list of available databases.</returns>
    public Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a database.
    /// </summary>
    /// <param name="databaseName">The name of the database to be dropped.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// Available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    public Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the flush state of multiple segments.
    /// </summary>
    /// <param name="segmentIds">A list of segment IDs for which to get the flush state</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>Whether the provided segments have been flushed.</returns>
    public Task<bool> GetFlushStateAsync(
        IReadOnlyList<long> segmentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes all collections. All insertions, deletions, and upserts prior to this call will be synced to disk.
    /// </summary>
    /// <remarks>
    /// While this method starts flushing, that process may not be complete when the method returns. The returned
    /// timestamp can be used to check on the state of the flush process, either via
    /// <see cref="GetFlushAllStateAsync" /> (for a single check) or via <see cref="WaitForFlushAllAsync" />
    /// (to wait until the process is complete).
    /// </remarks>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>
    /// A timestamp that can be passed to <see cref="GetFlushAllStateAsync" /> to check the state of the flush, or to
    /// <see cref="WaitForFlushAllAsync" /> to wait for it to complete.
    /// </returns>
    public Task<ulong> FlushAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether a flush initiated by a previous <see cref="FlushAllAsync(CancellationToken)" /> call has
    /// completed.
    /// </summary>
    /// <param name="timestamp">A timestamp value return by <see cref="FlushAllAsync(CancellationToken)" />.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<bool> GetFlushAllStateAsync(
        ulong timestamp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls Milvus for the state of a flush process initiated by a previous
    /// <see cref="FlushAllAsync(CancellationToken)" /> call, until that process is complete.
    /// </summary>
    /// <param name="timestamp">A timestamp value return by <see cref="FlushAllAsync(CancellationToken)" />.</param>
    /// <param name="waitingInterval">Waiting interval. Defaults to 500 milliseconds.</param>
    /// <param name="timeout">How long to poll for before throwing a <see cref="TimeoutException" />.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task WaitForFlushAllAsync(
        ulong timestamp,
        TimeSpan? waitingInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls Milvus for the flush state of the specific segments, until those segments are fully flushed.
    /// </summary>
    /// <param name="segmentIds">The segment IDs whose flush state should be polled..</param>
    /// <param name="waitingInterval">Waiting interval. Defaults to 500 milliseconds.</param>
    /// <param name="timeout">How long to poll for before throwing a <see cref="TimeoutException" />.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task WaitForFlushAsync(
        IReadOnlyList<long> segmentIds,
        TimeSpan? waitingInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics.
    /// </summary>
    /// <param name="request">Request in JSON format.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<MilvusMetrics> GetMetricsAsync(
        string request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <param name="roleName">The name of the role to be created.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task CreateRoleAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <param name="roleName">The name of the role to be dropped.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task DropRoleAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user to a role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <param name="username">The name of the username to be added to the role.</param>
    /// <param name="roleName">The name of the role the user will be added to.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task AddUserToRoleAsync(
        string username,
        string roleName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a user from a role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <param name="username">The name of the user to be removed from the role.</param>
    /// <param name="roleName">The name of the role from which the user is to be removed.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task RemoveUserFromRoleAsync(
        string username,
        string roleName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a role, including optionally all its users.
    /// </summary>
    /// <param name="roleName">The name of the role to be selected.</param>
    /// <param name="includeUserInfo">Whether to include user information in the results.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A <see cref="RoleResult" /> instance containing information about the role, or <c>null</c> if the role does not
    /// exist.
    /// </returns>
    public Task<RoleResult?> SelectRoleAsync(
        string roleName,
        bool includeUserInfo = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about all roles defined in Milvus, including optionally all their users.
    /// </summary>
    /// <param name="includeUserInfo">Whether to include user information in the results.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <returns>A list of <see cref="RoleResult" /> instances containing information about all the roles.</returns>
    public Task<IReadOnlyList<RoleResult>> SelectAllRolesAsync(
        bool includeUserInfo = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a user, including optionally all its roles.
    /// </summary>
    /// <param name="username">The name of the user to be selected.</param>
    /// <param name="includeRoleInfo">Whether to include role information in the results.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A <see cref="UserResult" /> instance containing information about the user, or <c>null</c> if the user does not
    /// exist.
    /// </returns>
    public Task<UserResult?> SelectUserAsync(
        string username,
        bool includeRoleInfo = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about all users defined in Milvus, including optionally all their users.
    /// </summary>
    /// <param name="includeRoleInfo">Whether to include role information in the results.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <returns>A list of <see cref="UserResult" /> instances containing information about all the users.</returns>
    public Task<IReadOnlyList<UserResult>> SelectAllUsersAsync(
        bool includeRoleInfo = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a privilege to a role.
    /// </summary>
    /// <param name="roleName">The name of the role to be granted a privilege.</param>
    /// <param name="object">
    /// A string describing the object type on which the privilege is to be granted, e.g. <c>"Collection"</c>.
    /// </param>
    /// <param name="objectName">
    /// A string describing the specific object on which the privilege will be granted. Can be <c>"*"</c>.
    /// </param>
    /// <param name="privilege">A string describing the privilege to be granted, e.g. <c>"Search"</c>.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    public Task GrantRolePrivilegeAsync(
        string roleName,
        string @object,
        string objectName,
        string privilege,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a privilege from a role.
    /// </summary>
    /// <param name="roleName">The name of the role to be revoked a privilege.</param>
    /// <param name="object">
    /// A string describing the object type on which the privilege is to be revoked, e.g. <c>"Collection"</c>.
    /// </param>
    /// <param name="objectName">
    /// A string describing the specific object on which the privilege will be revoked. Can be <c>"*"</c>.
    /// </param>
    /// <param name="privilege">A string describing the privilege to be revoked, e.g. <c>"Search"</c>.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    public Task RevokeRolePrivilegeAsync(
        string roleName,
        string @object,
        string objectName,
        string privilege,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a grant info for the role and the specific object.
    /// </summary>
    /// <param name="roleName">The name of the role.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <remarks>
    /// <para>
    /// For more details, see <see href="https://milvus.io/docs/rbac.md" />.
    /// </para>
    /// <para>
    /// Roles are available starting Milvus 2.2.9.
    /// </para>
    /// </remarks>
    /// <returns>A list of <see cref="GrantEntity" /> instances describing the grants assigned to the role.</returns>
    public Task<IReadOnlyList<GrantEntity>> ListGrantsForRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List a grant info for the role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// available in <c>Milvus 2.2.9</c>
    /// </para>
    /// </remarks>
    /// <param name="roleName">RoleName cannot be empty or null.</param>
    /// <param name="object">object. object cannot be empty or null.</param>
    /// <param name="objectName">objectName. objectName cannot be empty or null.</param>
    /// <param name="cancellationToken">Cancellation name.</param>
    public Task<IReadOnlyList<GrantEntity>> SelectGrantForRoleAndObjectAsync(
        string roleName,
        string @object,
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user.
    /// </summary>
    /// <param name="username">The username of the user to be created.</param>
    /// <param name="password">The password of the user to be created..</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task CreateUserAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user.
    /// </summary>
    /// <param name="username">The username of the user to delete.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task DeleteUserAsync(
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the password for a user.
    /// </summary>
    /// <param name="username">The username for which to change the password.</param>
    /// <param name="oldPassword">The user's old password.</param>
    /// <param name="newPassword">The new password to set for the user.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task UpdatePassword(
        string username,
        string oldPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all users.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<IReadOnlyList<string>> ListUsernames(CancellationToken cancellationToken = default);
}
