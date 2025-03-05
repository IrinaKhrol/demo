using System.Text.Json.Serialization;
using Amazon.DynamoDBv2.DataModel;

namespace SimpleLambdaFunction;

public class TableDTO
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("number")]
    public int Number { get; set; }
    
    [JsonPropertyName("places")]
    public int Places { get; set; }
    
    [JsonPropertyName("isVip")]
    public bool IsVip { get; set; }
    
    [JsonPropertyName("minOrder")] 
    public int MinOrder { get; set; }
}

public class TableDynamoDB
{
    [DynamoDBHashKey("id")]
    public string Id { get; set; }
    
    [DynamoDBProperty("number")]
    public int Number { get; set; }
    
    [DynamoDBProperty("places")]
    public int Places { get; set; }
    
    [DynamoDBProperty("isVip")]
    public bool IsVip { get; set; }
    
    [DynamoDBProperty("minOrder")] //optional
    public int MinOrder { get; set; }
}