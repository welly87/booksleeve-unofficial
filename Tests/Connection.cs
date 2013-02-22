using BookSleeve;
using NUnit.Framework;
using System.Linq;
using System;
using System.Diagnostics;
using System.IO;

namespace Tests
{
    [TestFixture]
    public class Connections // http://redis.io/commands#connection
    {
        [Test]
        public void TestConnectViaSentinel()
        {
            string[] endpoints;
            StringWriter sw = new StringWriter();
            var selected = ConnectionUtils.SelectConfiguration("192.168.0.19:26379,serviceName=mymaster", out endpoints, sw);
            string log = sw.ToString();
            Console.WriteLine(log);
            Assert.AreEqual("192.168.0.19:6379", selected);
        }
        [Test]
        public void TestConnectViaSentinelInvalidServiceName()
        {
            string[] endpoints;
            StringWriter sw = new StringWriter();
            var selected = ConnectionUtils.SelectConfiguration("192.168.0.19:26379,serviceName=garbage", out endpoints, sw);
            string log = sw.ToString();
            Console.WriteLine(log);
            Assert.IsNull(selected);
        }
        [Test]
        public void TestDirectConnect()
        {
            string[] endpoints;
            StringWriter sw = new StringWriter();
            var selected = ConnectionUtils.SelectConfiguration("192.168.0.19:6379", out endpoints, sw);
            string log = sw.ToString();
            Console.WriteLine(log);
            Assert.AreEqual("192.168.0.19:6379", selected);

        }

        [Test]
        public void TestName()
        {
            using (var conn = Config.GetUnsecuredConnection(open: false, allowAdmin: true))
            {
                string name = Guid.NewGuid().ToString().Replace("-","");
                conn.Name = name;
                conn.Wait(conn.Open());
                if (conn.Features.ClientName)
                {
                    var client = conn.Wait(conn.Server.ListClients()).SingleOrDefault(c => c.Name == name);
                    Assert.IsNotNull(client);
                }
            }
        }
        [Test]
        public void TestNameViaConnect()
        {
            string name = Guid.NewGuid().ToString().Replace("-","");
            using (var conn = ConnectionUtils.Connect("192.168.0.10,allowAdmin=true,name=" + name))
            {
                Assert.AreEqual(name, conn.Name);
                if (conn.Features.ClientName)
                {
                    var client = conn.Wait(conn.Server.ListClients()).SingleOrDefault(c => c.Name == name);
                    Assert.IsNotNull(client);
                }
            }
        }

        // AUTH is already tested by secured connection

        // QUIT is implicit in dispose

        // ECHO has little utility in an application

        [Test]
        public void TestGetSetOnDifferentDbHasDifferentValues()
        {
            // note: we don't expose SELECT directly, but we can verify that we have different DBs in play:

            using (var conn = Config.GetUnsecuredConnection())
            {
                conn.Strings.Set(1, "select", "abc");
                conn.Strings.Set(2, "select", "def");
                var x = conn.Strings.GetString(1, "select");
                var y = conn.Strings.GetString(2, "select");
                conn.WaitAll(x, y);
                Assert.AreEqual("abc", x.Result);
                Assert.AreEqual("def", y.Result);
            }
        }
        [Test, ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void TestGetOnInvalidDbThrows()
        {
            using (var conn = Config.GetUnsecuredConnection())
            {
                conn.Strings.GetString(-1, "select");                
            }
        }


        [Test]
        public void Ping()
        {
            using (var conn = Config.GetUnsecuredConnection())
            {
                var ms = conn.Wait(conn.Server.Ping());
                Assert.GreaterOrEqual(ms, 0);
            }
        }

        [Test]
        public void CheckCounters()
        {
            using (var conn = Config.GetUnsecuredConnection())
            {
                conn.Wait(conn.Strings.GetString(0, "select"));
                var first = conn.GetCounters();

                conn.Wait(conn.Strings.GetString(0, "select"));
                var second = conn.GetCounters();
                // +2 = ping + one select
                Assert.AreEqual(first.MessagesSent + 2, second.MessagesSent, "MessagesSent");
                Assert.AreEqual(first.MessagesReceived + 2, second.MessagesReceived, "MessagesReceived");
                Assert.AreEqual(0, second.ErrorMessages, "ErrorMessages");
                Assert.AreEqual(0, second.MessagesCancelled, "MessagesCancelled");
                Assert.AreEqual(0, second.SentQueue, "SentQueue");
                Assert.AreEqual(0, second.UnsentQueue, "UnsentQueue");
                Assert.AreEqual(0, second.QueueJumpers, "QueueJumpers");
                Assert.AreEqual(0, second.Timeouts, "Timeouts");
                Assert.IsTrue(second.Ping >= 0, "Ping");
                Assert.IsTrue(second.ToString().Length > 0, "ToString");
            }
        }

        
    }
}
