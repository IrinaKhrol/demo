using System.Text.Json.Serialization;
using Amazon.DynamoDBv2.DataModel;

namespace SimpleLambdaFunction;

public class Reservations
{
    [DynamoDBHashKey("id")]
    [JsonPropertyName("reservationId")]
    [JsonIgnore]
    public string ReservationId { get; set; }

    [DynamoDBProperty("tableNumber")]
    [JsonPropertyName("tableNumber")]
    public int TableNumber { get; set; }

    [DynamoDBProperty("clientName")]
    [JsonPropertyName("clientName")]
    public string ClientName { get; set; }

    [DynamoDBProperty("phoneNumber")]
    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; }

    [DynamoDBProperty("date")]
    [JsonPropertyName("date")]
    public string Date { get; set; } // string in yyyy-MM-dd format

    [DynamoDBProperty("slotTimeStart")]
    [JsonPropertyName("slotTimeStart")]
    public string SlotTimeStart { get; set; } // string in "HH:MM" format, like "13:00"
    
    [DynamoDBProperty("slotTimeEnd")]
    [JsonPropertyName("slotTimeEnd")]
    public string SlotTimeEnd { get; set; } // string in "HH:MM" format, like "15:00"
}