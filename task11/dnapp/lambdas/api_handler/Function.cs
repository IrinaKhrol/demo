using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
    [DynamoDBTable("Tables")]
    public class Table
    {
        [DynamoDBHashKey]
        public int Id { get; set; }
        public int Number { get; set; }
        public int Places { get; set; }
        public bool IsVip { get; set; }
        public int? MinOrder { get; set; }
    }

    [DynamoDBTable("Reservations")]
    public class Reservation
    {
        [DynamoDBHashKey]
        public string? ReservationId { get; set; }
        public int TableNumber { get; set; }
        public string? ClientName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Date { get; set; }
        public string? SlotTimeStart { get; set; }
        public string? SlotTimeEnd { get; set; }
    }

    public class Function
    {
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly DynamoDBContext _dynamoContext;
        private readonly string? _userPoolId;
        private readonly string? _clientId;

        public Function()
        {
            _cognitoClient = new AmazonCognitoIdentityProviderClient();
            _dynamoDbClient = new AmazonDynamoDBClient();
            _dynamoContext = new DynamoDBContext(_dynamoDbClient);
            _userPoolId = Environment.GetEnvironmentVariable("cup_id");
            _clientId = Environment.GetEnvironmentVariable("cup_client_id");
        }

        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var path = request.Path.ToLower();
            var httpMethod = request.HttpMethod.ToUpper();

            switch (path, httpMethod)
            {
                case ("/signup", "POST"):
                    return await HandleSignup(request);
                case ("/signin", "POST"):
                    return await HandleSignin(request);
                case ("/tables", "GET"):
                    return await HandleGetTables(request);
                case ("/tables", "POST"):
                    return await HandleCreateTable(request);
                case ("/tables/{tableid}", "GET"):
                    return await HandleGetTableById(request);
                case ("/reservations", "POST"):
                    return await HandleCreateReservation(request);
                case ("/reservations", "GET"):
                    return await HandleGetReservations(request);
                default:
                    return CreateResponse(400, new { message = "Invalid endpoint" });
            }
        }

        private APIGatewayProxyResponse CreateResponse(int statusCode, object body)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = statusCode,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                Body = JsonSerializer.Serialize(body),
                IsBase64Encoded = false
            };
        }

        private async Task<bool> ValidateToken(APIGatewayProxyRequest request)
        {
            if (!request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return false;
            }

            var token = authHeader.Substring("Bearer ".Length);
            try
            {
                var requestParameters = new GetUserRequest { AccessToken = token };
                await _cognitoClient.GetUserAsync(requestParameters);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<APIGatewayProxyResponse> HandleSignup(APIGatewayProxyRequest request)
        {
            if (request.Body == null)
            {
                return CreateResponse(400, new { message = "Request body is null" });
            }

            var body = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body);
            if (!body.TryGetValue("email", out var email) || !body.TryGetValue("password", out var password) ||
                !body.TryGetValue("firstName", out var firstName) || !body.TryGetValue("lastName", out var lastName))
            {
                return CreateResponse(400, new { message = "Missing required fields" });
            }

            if (!email.Contains("@") || !email.Contains("."))
            {
                return CreateResponse(400, new { message = "Invalid email format" });
            }

            if (password.Length < 12 || !password.Any(c => "$%^*-_".Contains(c)))
            {
                return CreateResponse(400, new { message = "Password must be 12+ characters and include one of $%^*-_" });
            }

            try
            {
                var signUpRequest = new SignUpRequest
                {
                    ClientId = _clientId,
                    Username = email,
                    Password = password,
                    UserAttributes = new List<AttributeType>
                    {
                        new AttributeType { Name = "given_name", Value = firstName },
                        new AttributeType { Name = "family_name", Value = lastName },
                        new AttributeType { Name = "email", Value = email }
                    }
                };
                await _cognitoClient.SignUpAsync(signUpRequest);

                // Автоматическое подтверждение пользователя
                var confirmRequest = new AdminConfirmSignUpRequest
                {
                    UserPoolId = _userPoolId,
                    Username = email
                };
                await _cognitoClient.AdminConfirmSignUpAsync(confirmRequest);

                return CreateResponse(200, new { message = "Sign-up successful" });
            }
            catch (Exception ex)
            {
                return CreateResponse(400, new { message = ex.Message });
            }
        }

        private async Task<APIGatewayProxyResponse> HandleSignin(APIGatewayProxyRequest request)
        {
            if (request.Body == null)
            {
                return CreateResponse(400, new { message = "Request body is null" });
            }

            var body = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body);
            if (!body.TryGetValue("email", out var email) || !body.TryGetValue("password", out var password))
            {
                return CreateResponse(400, new { message = "Missing email or password" });
            }

            if (password.Length < 12 || !password.Any(c => "$%^*".Contains(c)))
            {
                return CreateResponse(400, new { message = "Password must be 12+ characters and include one of $%^*" });
            }

            try
            {
                var authRequest = new AdminInitiateAuthRequest
                {
                    UserPoolId = _userPoolId,
                    ClientId = _clientId,
                    AuthFlow = AuthFlowType.ADMIN_USER_PASSWORD_AUTH,
                    AuthParameters = new Dictionary<string, string>
                    {
                        { "USERNAME", email },
                        { "PASSWORD", password }
                    }
                };
                var authResponse = await _cognitoClient.AdminInitiateAuthAsync(authRequest);
                var accessToken = authResponse.AuthenticationResult.IdToken;
                return CreateResponse(200, new { accessToken });
            }
            catch (Exception ex)
            {
                return CreateResponse(400, new { message = ex.Message });
            }
        }

        private async Task<APIGatewayProxyResponse> HandleGetTables(APIGatewayProxyRequest request)
        {
            if (!await ValidateToken(request))
            {
                return CreateResponse(400, new { message = "Unauthorized" });
            }

            var tables = await _dynamoContext.ScanAsync<Table>(default).GetRemainingAsync();
            return CreateResponse(200, new { tables });
        }

        private async Task<APIGatewayProxyResponse> HandleCreateTable(APIGatewayProxyRequest request)
        {
            if (!await ValidateToken(request))
            {
                return CreateResponse(400, new { message = "Unauthorized" });
            }

            if (request.Body == null)
            {
                return CreateResponse(400, new { message = "Request body is null" });
            }

            var table = JsonSerializer.Deserialize<Table>(request.Body);
            if (table.Id <= 0 || table.Number <= 0 || table.Places <= 0)
            {
                return CreateResponse(400, new { message = "Invalid table data" });
            }

            await _dynamoContext.SaveAsync(table);
            return CreateResponse(200, new { id = table.Id });
        }

        private async Task<APIGatewayProxyResponse> HandleGetTableById(APIGatewayProxyRequest request)
        {
            if (!await ValidateToken(request))
            {
                return CreateResponse(400, new { message = "Unauthorized" });
            }

            if (!request.PathParameters.TryGetValue("tableId", out var tableIdStr) || !int.TryParse(tableIdStr, out var tableId))
            {
                return CreateResponse(400, new { message = "Invalid tableId" });
            }

            var table = await _dynamoContext.LoadAsync<Table>(tableId);
            if (table == null)
            {
                return CreateResponse(400, new { message = "Table not found" });
            }
            return CreateResponse(200, table);
        }

        private async Task<APIGatewayProxyResponse> HandleCreateReservation(APIGatewayProxyRequest request)
        {
            if (!await ValidateToken(request))
            {
                return CreateResponse(400, new { message = "Unauthorized" });
            }

            if (request.Body == null)
            {
                return CreateResponse(400, new { message = "Request body is null" });
            }

            var reservation = JsonSerializer.Deserialize<Reservation>(request.Body);
            if (reservation.TableNumber <= 0 || string.IsNullOrEmpty(reservation.ClientName) || string.IsNullOrEmpty(reservation.PhoneNumber) ||
                !DateTime.TryParseExact(reservation.Date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _) ||
                !DateTime.TryParseExact(reservation.SlotTimeStart, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _) ||
                !DateTime.TryParseExact(reservation.SlotTimeEnd, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
            {
                return CreateResponse(400, new { message = "Invalid reservation data" });
            }

            reservation.ReservationId = Guid.NewGuid().ToString();
            await _dynamoContext.SaveAsync(reservation);
            return CreateResponse(200, new { reservationId = reservation.ReservationId });
        }

        private async Task<APIGatewayProxyResponse> HandleGetReservations(APIGatewayProxyRequest request)
        {
            if (!await ValidateToken(request))
            {
                return CreateResponse(400, new { message = "Unauthorized" });
            }

            var reservations = await _dynamoContext.ScanAsync<Reservation>(default).GetRemainingAsync();
            return CreateResponse(200, new { reservations });
        }
    }
}
