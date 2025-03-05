using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;

namespace SimpleLambdaFunction;

public class AuthenticationService
{
    private readonly AmazonCognitoIdentityProviderClient _cognitoClient;
    private readonly string? _clientId = Environment.GetEnvironmentVariable("cup_client_id");
    private readonly string? _userPollId = Environment.GetEnvironmentVariable("cup_id");

    public AuthenticationService()
    {
        _cognitoClient = new AmazonCognitoIdentityProviderClient();
    }
    
    public async Task SignUp(string firstName, string lastName, string email, string password)
    {
        Console.WriteLine("_clientId: " + _clientId);
        Console.WriteLine("_userPollId: " + _userPollId);
        var signUpRequest = new SignUpRequest
        {
            ClientId = _clientId,
            Username = email,
            Password = password,
            UserAttributes = new List<AttributeType>
            {
                new() { Name = "given_name", Value = firstName },
                new() { Name = "family_name", Value = lastName },
                new() { Name = "email", Value = email }
            }
        };
        Console.WriteLine("signUpRequest: " + JsonSerializer.Serialize(signUpRequest));

        await _cognitoClient.SignUpAsync(signUpRequest);

        var confirmRequest = new AdminConfirmSignUpRequest
        {
            UserPoolId = _userPollId,
            Username = email
        };
        
        await _cognitoClient.AdminConfirmSignUpAsync(confirmRequest);
    }
    
    public async Task<string> SignIn(string email, string password)
    {
        var authRequest = new AdminInitiateAuthRequest
        {
            AuthFlow = AuthFlowType.ADMIN_NO_SRP_AUTH,
            ClientId = _clientId,
            UserPoolId = _userPollId,
            AuthParameters = new Dictionary<string, string>
            {
                { "USERNAME", email },
                { "PASSWORD", password }
            }
        };
        
        Console.WriteLine("authRequest: " + JsonSerializer.Serialize(authRequest));

        try
        {
            var authResponse = await _cognitoClient.AdminInitiateAuthAsync(authRequest);
            return authResponse.AuthenticationResult.IdToken;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to log in: {ex}");
            throw;
        }
    }
}