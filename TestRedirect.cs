using ASP_Components.Middleware;
using ASP_Components.Models;
using ASP_Components.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using Xunit.Abstractions;

namespace ASP_Components.test
{
    public class TestRedirect
    {

        private readonly ITestOutputHelper _output;

        public TestRedirect(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TestRedirectConcurrency()
        {

            //Create Mock Service
            var mockService = new Mock<IRedirectService>();
            var mockData = new List<RedirectData>
            {
                new RedirectData { RedirectUrl = "/old", TargetUrl = "/new", UseRelative = false }
            };
            mockService.Setup(service => service.GetRedirectDataAsync())
                      .ReturnsAsync((IEnumerable<RedirectData>)mockData);


            var middleware = new RedirectMiddleware(null, null, mockService.Object, null);

            // Simulate 10 reading threads
            var readingTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    // Access _redirectData here (or any method that indirectly accesses it)
                    var data = middleware.GetType().GetField("_redirectData", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(middleware);
                    _output.WriteLine($"Thread {Task.CurrentId} - Reading data: {data}");
                    await Task.Delay(10);  // Simulate some delay
                }
            })).ToList();

            // Simulate updating thread
            var updatingTask = Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    _output.WriteLine($"Updating data...");
                    middleware.UpdateFromSourceAsync(); // Update _redirectData
                    await Task.Delay(50);  // Simulate some delay
                }
            });

            await Task.WhenAll(readingTasks.Concat(new[] { updatingTask }));
        }

        [Fact]
        public async Task Ensure_RedirectFunctionality_WorksAsExpected()
        {
            // Setup RedirectService mock
            var mockRedirectService = new Mock<IRedirectService>();
            mockRedirectService.Setup(service => service.GetRedirectDataAsync()).ReturnsAsync(new List<RedirectData>
            {
                new RedirectData {
                    RedirectUrl = "/campaignA",
                    TargetUrl = "/campaigns/targetcampaign",
                    RedirectType= 302,
                    UseRelative = false },
                new RedirectData {
                    RedirectUrl = "/campaignB",
                    TargetUrl = "/campaigns/targetcampaign/channelB",
                    RedirectType= 302,
                    UseRelative = false },
                new RedirectData {
                    RedirectUrl = "/product-directory",
                    TargetUrl = "/products",
                    RedirectType= 301,
                    UseRelative = true }
            });

            // Setup ILogger<RedirectMiddleware> mock
            var mockLogger = new Mock<ILogger<RedirectMiddleware>>();

            // Setup IConfiguration mock
            var mockConfig = new Mock<IConfiguration>();
            mockConfig.Setup(config => config["RedirectCacheLifespan_Minutes"]).Returns("5");

            // Instantiate the RedirectMiddleware
            var middleware = new RedirectMiddleware(null, mockLogger.Object, mockRedirectService.Object, mockConfig.Object);

            var tasks = new List<Task<bool>>();
            tasks.Add(checkMiddleWare(middleware,
                "/product-directory/bits/masonary/diamond-tip",
                "/products/bits/masonary/diamond-tip",
                StatusCodes.Status301MovedPermanently));

            tasks.Add(checkMiddleWare(middleware,
               "/campaignA",
               "/campaigns/targetcampaign",
               StatusCodes.Status302Found));

            tasks.Add(checkMiddleWare(middleware,
               "/campaignB",
               "/campaigns/targetcampaign/channelB",
               StatusCodes.Status302Found));

            Task.WaitAll(tasks.ToArray());

        }

        private async Task<bool> checkMiddleWare(
            RedirectMiddleware middleware,
            string requestedPath,
            string expectedResult,
            int expectedStatusCode
            )
        {

            // Setup mock HttpContext
            var context = new DefaultHttpContext();
            context.Request.Path = requestedPath;

            // Invoke the middleware
            await middleware.Invoke(context);

            // Assert the redirect
            Assert.Equal(expectedStatusCode, context.Response.StatusCode);
            Assert.Equal(expectedResult, context.Response.Headers["Location"]);

            _output.WriteLine($"Redirected {requestedPath} to {expectedResult} with code {context.Response.StatusCode}");
            return true;
        }

        [Fact]
        public async Task Ensure_Updating_Correctly()
        {
            List<Task> tasks = new List<Task>();
            tasks.Add(Update_With_Delay("0.05", 3000));
            tasks.Add(Update_With_Delay("0.1", 6000));
            tasks.Add(Update_With_Delay("0.2", 12000));
            tasks.Add(Update_With_Delay("0.3", 18000));
            Task.WaitAll(tasks.ToArray());
        }

        private async Task<bool> Update_With_Delay(string delayString, int waitSeconds)
        {
            // Setup RedirectService mock
            var mockRedirectService = new Mock<IRedirectService>();
            mockRedirectService.Setup(service => service.GetRedirectDataAsync()).ReturnsAsync(new List<RedirectData>
            {
                new RedirectData {
                    RedirectUrl = "/campaignA",
                    TargetUrl = "/campaigns/targetcampaign",
                    RedirectType= 302,
                    UseRelative = false },
            });

            // Setup ILogger<RedirectMiddleware> mock
            var mockLogger = new Mock<ILogger<RedirectMiddleware>>();

            // Setup IConfiguration mock
            var mockConfig = new Mock<IConfiguration>();
            mockConfig.Setup(config => config["RedirectCacheLifespan_Minutes"]).Returns(delayString);

            // Instantiate the RedirectMiddleware
            var middleware = new RedirectMiddleware(null, mockLogger.Object, mockRedirectService.Object, mockConfig.Object);

            //Check initial state
            await checkMiddleWare(middleware,
                 "/campaignA",
                 "/campaigns/targetcampaign",
                 StatusCodes.Status302Found);

            //updates the redirect data
            mockRedirectService.Setup(service => service.GetRedirectDataAsync()).ReturnsAsync(new List<RedirectData>
            {
                new RedirectData {
                    RedirectUrl = "/campaignA",
                    TargetUrl = "/campaigns/targetcampaign2",
                    RedirectType= 302,
                    UseRelative = false },
            });

            //Check state again, should not have updated yet
            await checkMiddleWare(middleware,
                 "/campaignA",
                 "/campaigns/targetcampaign",
                 StatusCodes.Status302Found);

            //Wait for cache to expire
            await Task.Delay(waitSeconds);

            //Check state again, should have updated
            await checkMiddleWare(middleware,
              "/campaignA",
              "/campaigns/targetcampaign2",
              StatusCodes.Status302Found);

            return true;
        }

    }
}