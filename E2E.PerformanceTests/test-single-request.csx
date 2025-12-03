using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using E2E.PerformanceTests.Infrastructure;

var passengerId = Guid.NewGuid();
var token = JwtTokenHelper.GeneratePassengerToken(passengerId);

var requestBody = new
{
    pickupLat = 10.762622,
    pickupLng = 106.660172,
    dropoffLat = 10.773996,
    dropoffLng = 106.697214
};

var jsonBody = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });

Console.WriteLine("========== INPUT (Test Request) ==========");
Console.WriteLine($"URL: POST http://127.0.0.1:8080/api/trips");
Console.WriteLine($"Authorization: Bearer {token.Substring(0, 50)}...");
Console.WriteLine($"Content-Type: application/json");
Console.WriteLine($"\nRequest Body:");
Console.WriteLine(jsonBody);
Console.WriteLine();

using var client = new HttpClient();
client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
var response = await client.PostAsync("http://127.0.0.1:8080/api/trips", content);

Console.WriteLine("========== OUTPUT (API Response) ==========");
Console.WriteLine($"Status Code: {(int)response.StatusCode} {response.StatusCode}");
Console.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
Console.WriteLine($"\nResponse Body:");
var responseBody = await response.Content.ReadAsStringAsync();
var formattedResponse = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(responseBody), new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(formattedResponse);
