using BookSleeve;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests
{
    [TestFixture]
    public class Scripting
    {
        static RedisConnection GetScriptConn(bool allowAdmin = false)
        {
            var conn = Config.GetUnsecuredConnection(waitForOpen: true, allowAdmin: allowAdmin);
            if (!conn.Features.Scripting)
            {
                using (conn) { return null; }
            }
            return conn;

        }
        [Test]
        public void BasicScripting()
        {
            using (var conn = GetScriptConn())
            {
                if (conn == null) return;

                var noCache = conn.Scripting.Eval(0, "return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
                    new[] { "key1", "key2" }, new[] { "first", "second" }, useCache: false);
                var cache = conn.Scripting.Eval(0, "return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}",
                    new[] { "key1", "key2" }, new[] { "first", "second" }, useCache: true);
                var results = (object[])conn.Wait(noCache);
                Assert.AreEqual(4, results.Length);
                Assert.AreEqual("key1", results[0]);
                Assert.AreEqual("key2", results[1]);
                Assert.AreEqual("first", results[2]);
                Assert.AreEqual("second", results[3]);

                results = (object[])conn.Wait(cache);
                Assert.AreEqual(4, results.Length);
                Assert.AreEqual("key1", results[0]);
                Assert.AreEqual("key2", results[1]);
                Assert.AreEqual("first", results[2]);
                Assert.AreEqual("second", results[3]);
            }
        }
        [Test]
        public void KeysScripting()
        {
            using (var conn = GetScriptConn())
            {
                if (conn == null) return;
                conn.Strings.Set(0, "foo", "bar");
                var result = (string)conn.Wait(conn.Scripting.Eval(0, "return redis.call('get', KEYS[1])", new[] { "foo" }, null));
                Assert.AreEqual("bar", result);
            }
        }

        [Test]
        public void HackyGetPerf()
        {
            using (var conn = GetScriptConn())
            {
                if (conn == null) return;
                conn.Strings.Set(0, "foo", "bar");
                var key = Guid.NewGuid().ToString(); // 
                var result = (long)conn.Wait(conn.Scripting.Eval(0, @"
redis.call('psetex', KEYS[1], 60000, 'timing')
for i = 1,100000 do
    redis.call('set', 'ignore','abc')
end
local timeTaken = 60000 - redis.call('pttl', KEYS[1])
redis.call('del', KEYS[1])
return timeTaken
", new[] { key }, null));
                Console.WriteLine(result);
                Assert.IsTrue(result > 0);
            }
        }

        [Test]
        public void MultiIncrWithoutReplies()
        {
            using (var conn = GetScriptConn())
            {
                if (conn == null) return; // not 2.6

                const int DB = 0; // any database number
                // prime some initial values
                conn.Keys.Remove(DB, new[] { "a", "b", "c" });
                conn.Strings.Increment(DB, "b");
                conn.Strings.Increment(DB, "c");
                conn.Strings.Increment(DB, "c");

                // run the script, passing "a", "b", "c", "c" to
                // increment a & b by 1, c twice
                var result = conn.Scripting.Eval(DB,
                    @"for i,key in ipairs(KEYS) do redis.call('incr', key) end",
                    new[] { "a", "b", "c", "c" }, // <== aka "KEYS" in the script
                    null); // <== aka "ARGV" in the script

                // check the incremented values
                var a = conn.Strings.GetInt64(DB, "a");
                var b = conn.Strings.GetInt64(DB, "b");
                var c = conn.Strings.GetInt64(DB, "c");

                Assert.IsNull(conn.Wait(result), "result");
                Assert.AreEqual(1, conn.Wait(a), "a");
                Assert.AreEqual(2, conn.Wait(b), "b");
                Assert.AreEqual(4, conn.Wait(c), "c");
            }
        }

        [Test]
        public void MultiIncrByWithoutReplies()
        {
            using (var conn = GetScriptConn())
            {
                if (conn == null) return; // not 2.6

                const int DB = 0; // any database number
                // prime some initial values
                conn.Keys.Remove(DB, new[] { "a", "b", "c" });
                conn.Strings.Increment(DB, "b");
                conn.Strings.Increment(DB, "c");
                conn.Strings.Increment(DB, "c");

                // run the script, passing "a", "b", "c" and 1,2,3
                // increment a & b by 1, c twice
                var result = conn.Scripting.Eval(DB,
                    @"for i,key in ipairs(KEYS) do redis.call('incrby', key, ARGV[i]) end",
                    new[] { "a", "b", "c" }, // <== aka "KEYS" in the script
                    new object[] {1,1,2}); // <== aka "ARGV" in the script

                // check the incremented values
                var a = conn.Strings.GetInt64(DB, "a");
                var b = conn.Strings.GetInt64(DB, "b");
                var c = conn.Strings.GetInt64(DB, "c");

                Assert.IsNull(conn.Wait(result), "result");
                Assert.AreEqual(1, conn.Wait(a), "a");
                Assert.AreEqual(2, conn.Wait(b), "b");
                Assert.AreEqual(4, conn.Wait(c), "c");
            }
        }

        [Test]
        public void DisableStringInference()
        {
            using (var conn = GetScriptConn())
            {
                if (conn == null) return;
                conn.Strings.Set(0, "foo", "bar");
                var result = (byte[])conn.Wait(conn.Scripting.Eval(0, "return redis.call('get', KEYS[1])", new[] { "foo" }, null, inferStrings: false));
                Assert.AreEqual("bar", Encoding.UTF8.GetString(result));
            }
        }

        [Test]
        public void FlushDetection()
        { // we don't expect this to handle everything; we just expect it to be predictable
            using (var conn = GetScriptConn(allowAdmin: true))
            {
                if (conn == null) return;
                conn.Strings.Set(0, "foo", "bar");
                var result = conn.Wait(conn.Scripting.Eval(0, "return redis.call('get', KEYS[1])", new[] { "foo" }, null));
                Assert.AreEqual("bar", result);

                // now cause all kinds of problems
                conn.Server.FlushScriptCache();

                // expect this one to fail
                try {
                    conn.Wait(conn.Scripting.Eval(0, "return redis.call('get', KEYS[1])", new[] { "foo" }, null));
                    Assert.Fail("Shouldn't have got here");
                }
                catch (RedisException) { }
                catch { Assert.Fail("Expected RedisException"); }

                result = conn.Wait(conn.Scripting.Eval(0, "return redis.call('get', KEYS[1])", new[] { "foo" }, null));
                Assert.AreEqual("bar", result);
            }
        }

        [Test]
        public void PrepareScript()
        {
            string[] scripts = { "return redis.call('get', KEYS[1])", "return {KEYS[1],KEYS[2],ARGV[1],ARGV[2]}" };
            using (var conn = GetScriptConn(allowAdmin: true))
            {
                if (conn == null) return;
                conn.Server.FlushScriptCache();

                // when vanilla
                conn.Wait(conn.Scripting.Prepare(scripts));

                // when known to exist
                conn.Wait(conn.Scripting.Prepare(scripts));
            }
            using (var conn = GetScriptConn())
            {
                // when vanilla
                conn.Wait(conn.Scripting.Prepare(scripts));

                // when known to exist
                conn.Wait(conn.Scripting.Prepare(scripts));

                // when known to exist
                conn.Wait(conn.Scripting.Prepare(scripts));
            }
        }
        [Test]
        public void NonAsciiScripts()
        {
            using (var conn = GetScriptConn())
            {
                const string evil = "return '僕'";
                if (conn == null) return;

                var task = conn.Scripting.Prepare(evil);
                conn.Wait(task);
                var result = conn.Wait(conn.Scripting.Eval(0, evil, null, null));
                Assert.AreEqual("僕", result);
            }
        }

        [Test]
        public void ChangeDbInScript()
        {
            using (var conn = GetScriptConn())
            {
                if (conn == null) return;

                conn.Strings.Set(1, "foo", "db 1");
                conn.Strings.Set(2, "foo", "db 2");

                var evalResult = conn.Scripting.Eval(2, @"redis.call('select', 1)
return redis.call('get','foo')", null, null);
                var getResult = conn.Strings.GetString(2, "foo");

                Assert.AreEqual("db 1", conn.Wait(evalResult));
                // now, our connection thought it was in db 2, but the script changed to db 1
                Assert.AreEqual("db 2", conn.Wait(getResult));
                
            }
        }
    }
}
