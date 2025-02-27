using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
    public class Function
    {
        private readonly AmazonDynamoDBClient _dynamoDbClient;
        private readonly string _auditTableName;

        public Function()
        {
            _dynamoDbClient = new AmazonDynamoDBClient();
            
            var tableName = Environment.GetEnvironmentVariable("table_name");
            if (string.IsNullOrEmpty(tableName))
            {
                throw new Exception("table_name environment variable is not set");
            }
            
            _auditTableName = tableName;
        }

        public async Task FunctionHandler(DynamoDBEvent dynamoEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Processing {dynamoEvent.Records.Count} records...");
            context.Logger.LogLine($"Используется таблица аудита: {_auditTableName}");

            foreach (var record in dynamoEvent.Records)
            {
                try
                {
                    context.Logger.LogLine($"Processing record with event name: {record.EventName}");
                    
                    if (record.EventName == "INSERT")
                    {
                        await HandleInsert(record, context);
                    }
                    else if (record.EventName == "MODIFY")
                    {
                        await HandleModify(record, context);
                    }
                    else
                    {
                        context.Logger.LogLine($"Unsupported event type: {record.EventName}");
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"Error processing record: {ex.Message}");
                    context.Logger.LogLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        private async Task HandleInsert(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            var newImage = record.Dynamodb.NewImage;
            context.Logger.LogLine($"Handling INSERT for record with key: {newImage["key"].S}");

            var modificationTime = DateTime.UtcNow;
            modificationTime = modificationTime.AddTicks(-(modificationTime.Ticks % TimeSpan.TicksPerMillisecond));
            var formattedTime = modificationTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            if (!newImage.ContainsKey("key") || !newImage.ContainsKey("value"))
            {
                context.Logger.LogLine("Error: Missing required fields in record.");
                return;
            }

            var auditItem = new Dictionary<string, AttributeValue>
            {
                { "id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                { "itemKey", new AttributeValue { S = newImage["key"].S } },
                { "modificationTime", new AttributeValue { S = formattedTime } },
                { "newValue", new AttributeValue
                    { 
                        M = new Dictionary<string, AttributeValue>
                        {
                            { "key", new AttributeValue { S = newImage["key"].S } },
                             { "value", new AttributeValue { N = Convert.ToInt32(newImage["value"].N).ToString() } }
                        }
                    } 
                }
            };

            await PutItemInAuditTable(auditItem, context);
        }

        private async Task HandleModify(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            var oldImage = record.Dynamodb.OldImage;
            var newImage = record.Dynamodb.NewImage;
            context.Logger.LogLine($"Handling MODIFY for record with key: {newImage["key"].S}");

            // Ensure required fields exist
            if (!oldImage.ContainsKey("value") || !newImage.ContainsKey("value"))
            {
                context.Logger.LogLine("Error: Missing 'value' field in record.");
                return;
            }

            var oldValue = int.Parse(oldImage["value"].N);
            var newValue = int.Parse(newImage["value"].N);

            var modificationTime = DateTime.UtcNow;
            modificationTime = modificationTime.AddTicks(-(modificationTime.Ticks % TimeSpan.TicksPerMillisecond));
            var formattedTime = modificationTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            var auditItem = new Dictionary<string, AttributeValue>{    
                { "id", new AttributeValue { S = Guid.NewGuid().ToString() } },        
                { "itemKey", new AttributeValue { S = newImage["key"].S } },        
                { "modificationTime", new AttributeValue { S = formattedTime } },        
                { "updatedAttribute", new AttributeValue { S = "value" } },        
                { "oldValue", new AttributeValue { N = oldValue.ToString() } },        
                { "newValue", new AttributeValue  { N = Convert.ToInt32(newImage["value"].N).ToString() } }};
 

            await PutItemInAuditTable(auditItem, context);
        }

        private async Task PutItemInAuditTable(Dictionary<string, AttributeValue> auditItem, ILambdaContext context)
        {
            context.Logger.LogLine($"Putting item in table: {_auditTableName}");
            
            var putRequest = new PutItemRequest
            {
                TableName = _auditTableName,
                Item = auditItem
            };

            try
            {
                await _dynamoDbClient.PutItemAsync(putRequest);
                context.Logger.LogLine($"Successfully created audit entry in table {_auditTableName}");
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error creating audit entry in table {_auditTableName}: {ex.Message}");
                context.Logger.LogLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }
    }
}

