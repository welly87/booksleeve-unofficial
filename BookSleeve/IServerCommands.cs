using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace BookSleeve
{
    /// <summary>
    ///     Commands related to server operation and configuration, rather than data.
    /// </summary>
    /// <remarks>http://redis.io/commands#server</remarks>
    public interface IServerCommands
    {
        /// <summary>
        ///     Delete all the keys of the currently selected DB.
        /// </summary>
        /// <remarks>http://redis.io/commands/flushdb</remarks>
        Task FlushDb(int db);

        /// <summary>
        ///     Delete all the keys of all the existing databases, not just the currently selected one.
        /// </summary>
        /// <remarks>http://redis.io/commands/flushall</remarks>
        Task FlushAll();

        /// <summary>
        ///     This command is often used to test if a connection is still alive, or to measure latency.
        /// </summary>
        /// <returns>The latency in milliseconds.</returns>
        /// <remarks>http://redis.io/commands/ping</remarks>
        Task<long> Ping(bool queueJump = false);

        /// <summary>
        ///     Get all configuration parameters matching the specified pattern.
        /// </summary>
        /// <param name="pattern">All the configuration parameters matching this parameter are reported as a list of key-value pairs.</param>
        /// <returns>All matching configuration parameters.</returns>
        /// <remarks>http://redis.io/commands/config-get</remarks>
        Task<Dictionary<string, string>> GetConfig(string pattern);

        /// <summary>
        ///     The CONFIG SET command is used in order to reconfigure the server at runtime without the need to restart Redis. You can change both trivial parameters or switch from one to another persistence option using this command.
        /// </summary>
        /// <remarks>http://redis.io/commands/config-set</remarks>
        Task SetConfig(string parameter, string value);

        /// <summary>
        ///     The SLAVEOF command can change the replication settings of a slave on the fly. In the proper form SLAVEOF hostname port will make the server a slave of another server listening at the specified hostname and port.
        ///     If a server is already a slave of some master, SLAVEOF hostname port will stop the replication against the old server and start the synchronization against the new one, discarding the old dataset.
        /// </summary>
        /// <remarks>http://redis.io/commands/slaveof</remarks>
        Task MakeSlave(string host, int port);

        /// <summary>
        ///     The SLAVEOF command can change the replication settings of a slave on the fly.
        ///     If a Redis server is already acting as slave, the command SLAVEOF NO ONE will turn off the replication, turning the Redis server into a MASTER.
        ///     The form SLAVEOF NO ONE will stop replication, turning the server into a MASTER, but will not discard the replication. So, if the old master stops working, it is possible to turn the slave into a master and set the application to use this new master in read/write. Later when the other Redis server is fixed, it can be reconfigured to work as a slave.
        /// </summary>
        Task MakeMaster();

        /// <summary>
        ///     Flush the Lua scripts cache; this can damage existing connections that expect the flush to behave normally, and should be used with caution.
        /// </summary>
        /// <remarks>http://redis.io/commands/script-flush</remarks>
        Task FlushScriptCache();

        /// <summary>
        ///     The CLIENT LIST command returns information and statistics about the client connections server in a mostly human readable format.
        /// </summary>
        /// <remarks>http://redis.io/commands/client-list</remarks>
        Task<ClientInfo[]> ListClients();

        /// <summary>
        ///     The CLIENT KILL command closes a given client connection identified by ip:port.
        /// </summary>
        /// <remarks>http://redis.io/commands/client-kill</remarks>
        Task KillClient(string address);
    }

    partial class RedisConnection : IServerCommands
    {
        /// <summary>
        ///     Commands related to server operation and configuration, rather than data.
        /// </summary>
        /// <remarks>http://redis.io/commands#server</remarks>
        public IServerCommands Server
        {
            get { return this; }
        }

        Task IServerCommands.FlushDb(int db)
        {
            CheckAdmin();
            return ExecuteVoid(RedisMessage.Create(db, RedisLiteral.FLUSHDB).ExpectOk().Critical(), false);
        }

        Task IServerCommands.FlushScriptCache()
        {
            CheckAdmin();
            return ExecuteVoid(RedisMessage.Create(-1, RedisLiteral.SCRIPT, RedisLiteral.FLUSH).ExpectOk(), false);
        }

        Task IServerCommands.KillClient(string address)
        {
            if (string.IsNullOrEmpty(address)) throw new ArgumentNullException("address");
            CheckAdmin();
            return ExecuteVoid(RedisMessage.Create(-1, RedisLiteral.CLIENT, RedisLiteral.KILL, address).ExpectOk(),
                               false);
        }

        Task<ClientInfo[]> IServerCommands.ListClients()
        {
            CheckAdmin();
            var result = new TaskCompletionSource<ClientInfo[]>();
            ExecuteString(RedisMessage.Create(-1, RedisLiteral.CLIENT, RedisLiteral.LIST), false).ContinueWith(
                task =>
                    {
                        if (Condition.ShouldSetResult(task, result))
                            try
                            {
                                result.TrySetResult(ClientInfo.Parse(task.Result));
                            }
                            catch (Exception ex)
                            {
                                result.TrySetException(ex);
                            }
                    });
            return result.Task;
        }

        Task IServerCommands.FlushAll()
        {
            CheckAdmin();
            return ExecuteVoid(RedisMessage.Create(-1, RedisLiteral.FLUSHALL).ExpectOk().Critical(), false);
        }

        Task IServerCommands.MakeMaster()
        {
            CheckAdmin();
            return
                ExecuteVoid(
                    RedisMessage.Create(-1, RedisLiteral.SLAVEOF, RedisLiteral.NO, RedisLiteral.ONE)
                                .ExpectOk()
                                .Critical(), false);
        }

        Task IServerCommands.MakeSlave(string host, int port)
        {
            CheckAdmin();
            return ExecuteVoid(RedisMessage.Create(-1, RedisLiteral.SLAVEOF, host, port).ExpectOk().Critical(), false);
        }

        Task<long> IServerCommands.Ping(bool queueJump)
        {
            return base.Ping(queueJump);
        }

        Task<Dictionary<string, string>> IServerCommands.GetConfig(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) pattern = "*";
            return ExecuteStringPairs(RedisMessage.Create(-1, RedisLiteral.CONFIG, RedisLiteral.GET, pattern), false);
        }

        Task IServerCommands.SetConfig(string name, string value)
        {
            CheckAdmin();
            return ExecuteVoid(RedisMessage.Create(-1, RedisLiteral.CONFIG, RedisLiteral.SET, name, value).ExpectOk(),
                               false);
        }

        private void CheckAdmin()
        {
            if (!allowAdmin)
                throw new InvalidOperationException(
                    "This command is not available unless the connection is created with admin-commands enabled");
        }

        /// <summary>
        ///     Delete all the keys of the currently selected DB.
        /// </summary>
        [Obsolete("Please use the Server API", false), EditorBrowsable(EditorBrowsableState.Never)]
        public Task FlushDb(int db)
        {
            return Server.FlushDb(db);
        }

        /// <summary>
        ///     Delete all the keys of all the existing databases, not just the currently selected one.
        /// </summary>
        [Obsolete("Please use the Server API", false), EditorBrowsable(EditorBrowsableState.Never)]
        public Task FlushAll()
        {
            return Server.FlushAll();
        }

        /// <summary>
        ///     This command is often used to test if a connection is still alive, or to measure latency.
        /// </summary>
        /// <returns>The latency in milliseconds.</returns>
        /// <remarks>http://redis.io/commands/ping</remarks>
        [Obsolete("Please use the Server API", false), EditorBrowsable(EditorBrowsableState.Never)]
        public new Task<long> Ping(bool queueJump = false)
        {
            return Server.Ping(queueJump);
        }
    }
}