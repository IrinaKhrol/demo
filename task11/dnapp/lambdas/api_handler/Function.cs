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
    public class Table
    {
        [DynamoDBHashKey("id")]
        public string Id { get; set; }
        [DynamoDBProperty("number")]
        public int Number { get; set; }
        [DynamoDBProperty("places")]
        public int Places { get; set; }
        [DynamoDBProperty("isVip")]
        public bool IsVip { get; set; }
        [DynamoDBProperty("minOrder")]
        public int MinOrder { get; set; }
    }

    [DynamoDBTable("Reservations")]
    public class Reservation
    {
        [DynamoDBHashKey("reservationId")]
        public string ReservationId { get; set; }
        
        [DynamoDBProperty("tableNumber")]
        public int TableNumber { get; set; }
        
        [DynamoDBProperty("clientName")]
        public string ClientName { get; set; }
        
        [DynamoDBProperty("phoneNumber")]
        public string PhoneNumber { get; set; }
        
        [DynamoDBProperty("date")]
        public string Date { get; set; }
        
        [DynamoDBProperty("slotTimeStart")]
        public string SlotTimeStart { get; set; }
        
        [DynamoDBProperty("slotTimeEnd")]
        public string SlotTimeEnd { get; set; }
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

            if (password.Length < 12 || !password.Any(c => "$%^*-_".Contains(c)))
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

        public async Task<int> CreateTable(int id, int number, int places, bool isVip, int minOrder)
        {
            var tableName = Environment.GetEnvironmentVariable("tables_db_table_name");
            var tableRequest = new Table()
            {
                Id = id.ToString(),
                Number = number,
                Places = places,
                IsVip = isVip,
                MinOrder = minOrder
            };
            
            var config = new DynamoDBOperationConfig
            {
                OverrideTableName = tableName
            };
            
            await _dynamoContext.SaveAsync(tableRequest, config);
            return id;
        }

        private async Task<APIGatewayProxyResponse> HandleCreateTable(APIGatewayProxyRequest request)
        {
            var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body??"{}");
            var id = body["id"].GetInt32();
            var number = body["number"].GetInt32();
            var places = body["places"].GetInt32();
            var isVip = body["isVip"].GetBoolean();
            var minOrder = body["minOrder"].GetInt32();
            try
            {                                
                int createdId = await CreateTable(id, number, places, isVip, minOrder);
                
                return CreateResponse(200, new { id = createdId });
            }
            catch (Exception ex)
            {
                return CreateResponse(400, new { message = $"Error creating table: {ex.Message}" });
            }
        }

        private async Task<APIGatewayProxyResponse> HandleGetTableById(APIGatewayProxyRequest request)
        {
            if (!await ValidateToken(request))
            {
                return CreateResponse(400, new { message = "Unauthorized" });
            }

            if (!request.PathParameters.TryGetValue("tableId", out var tableIdStr))
            {
                return CreateResponse(400, new { message = "Invalid tableId" });
            }

            var table = await _dynamoContext.LoadAsync<Table>(tableIdStr);
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

            try
            {
                var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Body);
                
                if (!body.TryGetValue("tableNumber", out var tableNumberElement) || !tableNumberElement.TryGetInt32(out var tableNumber) || tableNumber <= 0 ||
                    !body.TryGetValue("clientName", out var clientNameElement) || string.IsNullOrEmpty(clientNameElement.GetString()) ||
                    !body.TryGetValue("phoneNumber", out var phoneNumberElement) || string.IsNullOrEmpty(phoneNumberElement.GetString()) ||
                    !body.TryGetValue("date", out var dateElement) || string.IsNullOrEmpty(dateElement.GetString()) ||
                    !body.TryGetValue("slotTimeStart", out var slotTimeStartElement) || string.IsNullOrEmpty(slotTimeStartElement.GetString()) ||
                    !body.TryGetValue("slotTimeEnd", out var slotTimeEndElement) || string.IsNullOrEmpty(slotTimeEndElement.GetString()))
                {
                    return CreateResponse(400, new { message = "Invalid reservation data" });
                }
                
                var date = dateElement.GetString();
                var slotTimeStart = slotTimeStartElement.GetString();
                var slotTimeEnd = slotTimeEndElement.GetString();
                
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _) ||
                    !DateTime.TryParseExact(slotTimeStart, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _) ||
                    !DateTime.TryParseExact(slotTimeEnd, "HH:mm", null, System.Globalization.DateTimeStyles.None, out _))
                {
                    return CreateResponse(400, new { message = "Invalid date or time format" });
                }
                
                var reservation = new Reservation
                {
                    ReservationId = Guid.NewGuid().ToString(),
                    TableNumber = tableNumber,
                    ClientName = clientNameElement.GetString(),
                    PhoneNumber = phoneNumberElement.GetString(),
                    Date = date,
                    SlotTimeStart = slotTimeStart,
                    SlotTimeEnd = slotTimeEnd
                };
                
                await _dynamoContext.SaveAsync(reservation);
                return CreateResponse(200, new { reservationId = reservation.ReservationId });
            }
            catch (Exception ex)
            {
                return CreateResponse(400, new { message = $"Error creating reservation: {ex.Message}" });
            }
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
