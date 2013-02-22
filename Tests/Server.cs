using System.Linq;
using BookSleeve;
using NUnit.Framework;
using System.Threading;
using System;

namespace Tests
{
    [TestFixture]
    public class Server // http://redis.io/commands#server
    {
        [Test]
        public void TestGetConfigAll()
        {
            using(var db = Config.GetUnsecuredConnection())
            {
                var pairs = db.Wait(db.Server.GetConfig("*"));
                Assert.Greater(1, 0); // I always get double-check which arg is which
                Assert.Greater(pairs.Count, 0);
            }
        }

        [Test, ExpectedException(typeof(TimeoutException), ExpectedMessage = "The operation has timed out; the connection is not open")]
        public void TimeoutMessageNotOpened()
        {
            using (var conn = Config.GetUnsecuredConnection(open: false))
            {
                conn.Wait(conn.Strings.Get(0, "abc"));
            }
        }

        [Test, ExpectedException(typeof(TimeoutException), ExpectedMessage = "The operation has timed out.")]
        public void TimeoutMessageNoDetail()
        {
            using (var conn = Config.GetUnsecuredConnection(open: true))
            {
                conn.IncludeDetailInTimeouts = false;
                conn.Keys.Remove(0, "noexist");
                conn.Lists.BlockingRemoveFirst(0, new[] { "noexist" }, 5);
                conn.Wait(conn.Strings.Get(0, "abc"));
            }
        }

        [Test, ExpectedException(typeof(TimeoutException), ExpectedMessage = "The operation has timed out; possibly blocked by: 0: BLPOP \"noexist\" 5")]
        public void TimeoutMessageWithDetail()
        {
            using (var conn = Config.GetUnsecuredConnection(open: true))
            {
                conn.IncludeDetailInTimeouts = true;
                conn.Keys.Remove(0, "noexist");
                conn.Lists.BlockingRemoveFirst(0, new[] { "noexist" }, 5);
                conn.Wait(conn.Strings.Get(0, "abc"));
            }
        }

        [Test]
        public void ClientList()
        {
            using (var killMe = Config.GetUnsecuredConnection())
            using (var conn = Config.GetUnsecuredConnection(allowAdmin: true))
            {
                killMe.Wait(killMe.Strings.GetString(7, "kill me quick"));
                var clients = conn.Wait(conn.Server.ListClients());
                var target = clients.Single(x => x.Database == 7);
                conn.Wait(conn.Server.KillClient(target.Address));
                Assert.IsTrue(clients.Length > 0);

                try
                {
                    killMe.Wait(killMe.Strings.GetString(7, "kill me quick"));
                    Assert.Fail("Should have been dead");
                }
                catch(Exception) { }
            }
        }

        [Test]
        public void TestKeepAlive()
        {
            string oldValue = null;
            try
            {
                using (var db = Config.GetUnsecuredConnection(allowAdmin:true))
                {
                    oldValue = db.Wait(db.Server.GetConfig("timeout")).Single().Value;
                    db.Server.SetConfig("timeout", "20");
                    var before = db.GetCounters();
                    Thread.Sleep(12 * 1000);
                    var after = db.GetCounters();
                    // 3 here is 2 * keep-alive, and one PING in GetCounters()
                    int sent = after.MessagesSent - before.MessagesSent;
                    Assert.GreaterOrEqual(1, 0);
                    Assert.GreaterOrEqual(sent, 3);
                    Assert.LessOrEqual(0, 4);
                    Assert.LessOrEqual(sent, 5);
                }
            } finally
            {
                if (oldValue != null)
                {
                    using (var db = Config.GetUnsecuredConnection(allowAdmin:true))
                    {
                        db.Server.SetConfig("timeout", oldValue);
                    }
                }
            }
        }

        [Test]
        public void TestMasterSlaveSetup()
        {
            using(var unsec = Config.GetUnsecuredConnection(true, true, true))
            using(var sec = Config.GetUnsecuredConnection(true, true, true))
            {
                var makeSlave = sec.Server.MakeSlave(unsec.Host, unsec.Port);
                var info = sec.Wait(sec.GetInfo());
                sec.Wait(makeSlave);
                Assert.IsTrue(info.Contains("role:slave"), "slave");
                Assert.IsTrue(info.Contains("master_host:" + unsec.Host), "host");
                Assert.IsTrue(info.Contains("master_port:" + unsec.Port), "port");
                var makeMaster = sec.Server.MakeMaster();
                info = sec.Wait(sec.GetInfo());
                sec.Wait(makeMaster);
                Assert.IsTrue(info.Contains("role:master"), "master");

            }
        }
    }
}
