using System.Net;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiSmokeTests(ApiFactory factory)
{
    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
