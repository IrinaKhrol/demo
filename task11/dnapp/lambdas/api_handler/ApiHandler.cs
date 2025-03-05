using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;

namespace SimpleLambdaFunction;

public class ApiHandler
{
    private readonly AuthenticationService _authenticationService;
    private readonly DynamodbService _dynamodbService;
    
    public ApiHandler()
    {
        _authenticationService = new AuthenticationService();
        _dynamodbService = new DynamodbService();
    }
    
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest eventRequest,
        ILambdaContext context)
    {
        Console.WriteLine("eventRequest: " + JsonSerializer.Serialize(eventRequest));

        var requestPath = eventRequest.Resource;
        var methodName = eventRequest.HttpMethod;
        
        Console.WriteLine("eventRequest.Resource: " + requestPath);
        Console.WriteLine("eventRequest.HttpMethod: " + methodName);

        var actionEndpointMapping =
            new Dictionary<string,
                Dictionary<string, Func<APIGatewayProxyRequest, Task<APIGatewayProxyResponse>>>>()
            {
                {
                    "/signup", new Dictionary<string, Func<APIGatewayProxyRequest, Task<APIGatewayProxyResponse>>>
                    {
                        { "POST", Signup }
                    }
                },
                {
                    "/signin", new Dictionary<string, Func<APIGatewayProxyRequest, Task<APIGatewayProxyResponse>>>
                    {
                        { "POST", Signin }
                    }
                },
                {
                    "/tables", new Dictionary<string, Func<APIGatewayProxyRequest, Task<APIGatewayProxyResponse>>>
                    {
                        { "POST", CreateTable },
                        { "GET", GetTables }
                    }
                },
                  {
                    "/tables/{tableId}", new Dictionary<string, Func<APIGatewayProxyRequest, Task<APIGatewayProxyResponse>>>
                    {
                        { "GET", GetTableById }
                    }
                },
                {
                    "/reservations", new Dictionary<string, Func<APIGatewayProxyRequest, Task<APIGatewayProxyResponse>>>
                    {
                        { "POST", CreateReservation },
                        { "GET", GetReservations }
                    }
                },
                
            };

            if (!actionEndpointMapping.TryGetValue(requestPath, out var resourceMethods) ||
            !resourceMethods.TryGetValue(methodName, out var action))
        {
            return InvalidEndpoint(requestPath, methodName);
        }

        if (!string.IsNullOrEmpty(eventRequest.Body))
        {
            eventRequest.Body = eventRequest.Body.Trim();
        }

        return await action(eventRequest);
    }
    
    private APIGatewayProxyResponse InvalidEndpoint(string path, string method)
    {
        return FormatResponse(400,
            new
            {
                message = $"Bad request syntax or unsupported method. Request path: {path}. HTTP method: {method}"
            });
    }
      private APIGatewayProxyResponse FormatResponse(int code, object response)
    {
        var responseString = JsonSerializer.Serialize(response);
        Console.WriteLine("JsonSerializer.Serialize(response): " + responseString);

        return new APIGatewayProxyResponse
        {
            StatusCode = code,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
            Body = responseString
        };
    }
    
    private List<string> ValidateRequestParams(string[] expected, Dictionary<string, JsonElement> received)
    {
        var missing = new List<string>();
        foreach (var param in expected)
        {
            if (!received.ContainsKey(param))
            {
                missing.Add(param);
            }
        }

        return missing;
    }
    private async Task<APIGatewayProxyResponse> Signup(APIGatewayProxyRequest request)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var requiredParams = new[] { "firstName", "lastName", "email", "password" };
        var missingParams = ValidateRequestParams(requiredParams, body);

        if (missingParams.Count > 0)
        {
            return FormatResponse(400,
                new { message = $"Missing required parameters: {string.Join(", ", missingParams)}" });
        }

        var firstName = body["firstName"].GetString();
        var lastName = body["lastName"].GetString();
        var email = body["email"].GetString();
        var password = body["password"].GetString();
        Console.WriteLine($"{firstName} {lastName} {email} {password}");

        try
        {
            await _authenticationService.SignUp(firstName, lastName, email, password);
            return FormatResponse(200, new { message = $"User {email} was created" });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return FormatResponse(400, new { message = $"Something went wrong when signing up: {ex.Message}" });
        }
    }
     private async Task<APIGatewayProxyResponse> Signin(APIGatewayProxyRequest request)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var requiredParams = new[] { "email", "password" };
        var missingParams = ValidateRequestParams(requiredParams, body);

        if (missingParams.Count > 0)
        {
            return FormatResponse(400,
                new { message = $"Missing required parameters: {string.Join(", ", missingParams)}" });
        }

        var email = body["email"].GetString();
        var password = body["password"].GetString();
        
        Console.WriteLine($"{email} {password}");

        try
        {
            var result = await _authenticationService.SignIn(email, password);
            return FormatResponse(200, new { accessToken = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return FormatResponse(400,
                new
                {
                    message =
                        "We encountered an issue while trying to log you in. Please try again in a few minutes."
                });
        }
    }
    
    private async Task<APIGatewayProxyResponse> CreateTable(APIGatewayProxyRequest request)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var requiredParams = new[] { "id", "number", "places", "isVip" };
        var missingParams = ValidateRequestParams(requiredParams, body);

        if (missingParams.Count > 0)
        {
            return FormatResponse(400,
                new { message = $"Missing required parameters: {string.Join(", ", missingParams)}" });
        }
        
        var id = body["id"].GetInt32();
        var number = body["number"].GetInt32();
        var places = body["places"].GetInt32();
        var isVip = body["isVip"].GetBoolean();
        var minOrder = body["minOrder"].GetInt32();

        Console.WriteLine($"{id}, {number}, {places}, {isVip}");

        try
        {
            var result = await _dynamodbService.CreateTable(id, number, places, isVip, minOrder);
            return FormatResponse(200, new { id = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return FormatResponse(400,
                new
                {
                    message =
                        "We encountered an issue while trying to create table."
                });
        }
    }
     private async Task<APIGatewayProxyResponse> GetTables(APIGatewayProxyRequest request)
    {
        try
        {
            var result = await _dynamodbService.GetTables();
            return FormatResponse(200, new { tables = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return FormatResponse(400,
                new
                {
                    message =
                        "We encountered an issue while trying to create table."
                });
        }
    }
     private async Task<APIGatewayProxyResponse> CreateReservation(APIGatewayProxyRequest request)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var requiredParams = new[] { "tableNumber", "clientName", "phoneNumber", "date", "slotTimeStart", "slotTimeEnd" };
        var missingParams = ValidateRequestParams(requiredParams, body);

        if (missingParams.Count > 0)
        {
            return FormatResponse(400,
                new { message = $"Missing required parameters: {string.Join(", ", missingParams)}" });
        }
        
        var tableNumber = body["tableNumber"].GetInt32();
        var clientName = body["clientName"].GetString();
        var phoneNumber = body["phoneNumber"].GetString();
        var date = body["date"].GetString();
        var slotTimeStart = body["slotTimeStart"].GetString();
        var slotTimeEnd = body["slotTimeEnd"].GetString();
        
        Console.WriteLine($"{tableNumber}, {clientName}, {phoneNumber}, {date}");

        try
        {
            var result = await _dynamodbService.CreateReservation(tableNumber, clientName,  date, slotTimeStart, slotTimeEnd, phoneNumber);
            return FormatResponse(200, new { reservationId = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return FormatResponse(400,
                new
                {
                    message =
                        "We encountered an issue while trying to create reservation."
                });

        }

    }
     private async Task<APIGatewayProxyResponse> GetReservations(APIGatewayProxyRequest request)
    {
        try
        {
            var result = await _dynamodbService.GetReservations();
            return FormatResponse(200, new { reservations = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return FormatResponse(400,
                new
                {
                    message =
                        "We encountered an issue while trying to create table."
                });
        }
    }
      private async Task<APIGatewayProxyResponse> GetTableById(APIGatewayProxyRequest request)
    {
        
        try
        {
            var tableId = request.PathParameters["tableId"];
            var result = await _dynamodbService.GetTableById(tableId);
            return FormatResponse(200, new
            {
                id = result.Id,
                number = result.Number,
                places = result.Places,
                isVip = result.IsVip,
                minOrder = result.MinOrder
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return FormatResponse(400,
                new
                {
                    message =
                        "We encountered an issue while trying to create table."
                });
        }
    }
}




    
    
