using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Accusoft.PrizmDoc.Net.Http.Tests
{
    [TestClass]
    public class AffinitySession_SendAsync_AffinityToken_Tests
    {
        static PrizmDocRestClient client;
        static FluentMockServer mockServer;

        [ClassInitialize]
        public static void BeforeAll(TestContext context)
        {
            mockServer = FluentMockServer.Start();
            client = new PrizmDocRestClient("http://localhost:" + mockServer.Ports.First());
            client.DefaultRequestHeaders.Add("Acs-Api-Key", System.Environment.GetEnvironmentVariable("API_KEY"));
        }

        [ClassCleanup]
        public static void AfterAll()
        {
            mockServer.Stop();
            mockServer.Dispose();
        }

        [TestInitialize]
        public void BeforeEach()
        {
            mockServer.Reset();
        }

        [DataTestMethod]
        [DataRow("application/json")]
        [DataRow("application/json; charset=utf-8")]
        public async Task SendAsync_automatically_finds_affinity_token_in_a_JSON_response_and_uses_it_in_subsequent_requests(string responseContentType)
        {
            const string AFFINITY_TOKEN = "example-affinity-token";
            var responseWithAffinityToken = Response.Create()
                        .WithStatusCode(200)
                        .WithHeader("Content-Type", responseContentType)
                        .WithBody("{ \"id\": 123, \"affinityToken\": \"" + AFFINITY_TOKEN + "\" }");

            mockServer
                .Given(Request.Create().WithPath("/wat").UsingPost())
                .RespondWith(responseWithAffinityToken);

            mockServer
                .Given(Request.Create().WithPath("/wat/123").UsingGet())
                .RespondWith(responseWithAffinityToken);

            var session = client.CreateAffinitySession();

            string originalAffinityToken = null;

            // First request
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/wat"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsFalse(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "An Accusoft-Affinity-Token header was incorrectly sent in the first request.");

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                originalAffinityToken = (string)obj["affinityToken"];
                Assert.AreEqual(AFFINITY_TOKEN, originalAffinityToken, "The mock server did not respond with the affinityToken value we expected. Something is wrong with this test.");
            }

            // Second request
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/wat/123"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsTrue(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "Having already received an affinityToken in the response to the first request, the second request failed to include an Accusoft-Affinity-Token header.");
                Assert.AreEqual(originalAffinityToken, response.RequestMessage.Headers.GetValues("Accusoft-Affinity-Token").FirstOrDefault(), "Having already received an affinityToken in the response to the first request, the second request included an Accusoft-Affinity-Token header but did not set it to the correct value.");
            }

            // Third request
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/wat/123"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsTrue(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "Having already received an affinityToken in the response to the first request, the third request failed to include an Accusoft-Affinity-Token header.");
                Assert.AreEqual(originalAffinityToken, response.RequestMessage.Headers.GetValues("Accusoft-Affinity-Token").FirstOrDefault(), "Having already received an affinityToken in the response to the first request, the third request included an Accusoft-Affinity-Token header but did not set it to the correct value.");
            }
        }

        [TestMethod]
        public async Task SendAsync_does_not_attempt_to_find_an_affinity_token_when_the_response_media_type_is_not_JSON()
        {
            const string NON_JSON_RESPONSE_CONTENT_TYPE = "text/plain";
            const string AFFINITY_TOKEN = "example-affinity-token";
            var responseWithNonJsonContentType = Response.Create()
                        .WithStatusCode(200)
                        .WithHeader("Content-Type", NON_JSON_RESPONSE_CONTENT_TYPE)
                        .WithBody("{ \"id\": 123, \"affinityToken\": \"" + AFFINITY_TOKEN + "\" }");

            mockServer
                .Given(Request.Create().WithPath("/wat").UsingPost())
                .RespondWith(responseWithNonJsonContentType);

            mockServer
                .Given(Request.Create().WithPath("/wat/123").UsingGet())
                .RespondWith(responseWithNonJsonContentType);

            var session = client.CreateAffinitySession();

            // First request
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/wat"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsFalse(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "An Accusoft-Affinity-Token header was incorrectly sent in the first request.");
            }

            // Second request
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/wat/123"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsFalse(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "An Accusoft-Affinity-Token header was incorrectly sent in the second request.");
            }

            // Third request
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/wat/123"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsFalse(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "An Accusoft-Affinity-Token header was incorrectly sent in the third request.");
            }
        }

        [DataTestMethod]
        [DataRow("application/json")]
        [DataRow("application/json; charset=utf-8")]
        public async Task SendAsync_gracefully_stops_looking_for_an_affinity_token_when_the_response_content_type_indicates_JSON_but_the_body_is_not_valid_JSON(string responseContentType)
        {
            var responseWhoseBodyIsNotActuallyValidJson = Response.Create()
                        .WithStatusCode(200)
                        .WithHeader("Content-Type", responseContentType)
                        .WithBody("This is not JSON");

            mockServer
                .Given(Request.Create().WithPath("/wat").UsingPost())
                .RespondWith(responseWhoseBodyIsNotActuallyValidJson);

            mockServer
                .Given(Request.Create().WithPath("/wat/123").UsingGet())
                .RespondWith(responseWhoseBodyIsNotActuallyValidJson);

            var session = client.CreateAffinitySession();

            // First request
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/wat"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsFalse(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "An Accusoft-Affinity-Token header was incorrectly sent in the first request.");
            }

            // Second request
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/wat/123"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsFalse(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "An Accusoft-Affinity-Token header was incorrectly sent in the second request.");
            }

            // Third request
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/wat/123"))
            using (var response = await session.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                Assert.IsFalse(response.RequestMessage.Headers.Contains("Accusoft-Affinity-Token"), "An Accusoft-Affinity-Token header was incorrectly sent in the third request.");
            }
        }
    }
}