using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System;
using System.Collections.Generic;
using System.Text.Json;
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
            _auditTableName = Environment.GetEnvironmentVariable("table_name") ?? "Audit";
        }

        public async Task FunctionHandler(DynamoDBEvent dynamoEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Processing {dynamoEvent.Records.Count} records...");

            foreach (var record in dynamoEvent.Records)
            {
                try
                {
                    context.Logger.LogLine($"EventName: {record.EventName}, EventSource: {record.EventSource}");
                    context.Logger.LogLine($"DynamoDB keys: {JsonSerializer.Serialize(record.Dynamodb.Keys)}");
                    context.Logger.LogLine($"DynamoDB NewImage: {JsonSerializer.Serialize(record.Dynamodb.NewImage)}");
                    
                    if (record.EventName == "INSERT")
                    {
                        await HandleInsert(record, context);
                    }
                    else if (record.EventName == "MODIFY")
                    {
                        await HandleModify(record, context);
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

            var modificationTime = DateTime.UtcNow;
            modificationTime = modificationTime.AddTicks(-(modificationTime.Ticks % TimeSpan.TicksPerMillisecond));
            var formattedTime = modificationTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            var auditItem = new Dictionary<string, AttributeValue>
            {
                { "id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                { "itemKey", new AttributeValue { S = newImage["key"].S } },
                { "modificationTime", new AttributeValue { S = formattedTime } },
                { "item.newValue", new AttributeValue 
                    { 
                        M = new Dictionary<string, AttributeValue>
                        {
                            { "key", new AttributeValue { S = newImage["key"].S } },
                            { "value", new AttributeValue { N = newImage["value"].N } }
                        }
                    } 
                }
            };

            await PutItemInAuditTable(auditItem, context);
            
            context.Logger.LogLine($"Audit item created: {JsonSerializer.Serialize(auditItem)}");
        }

        private async Task HandleModify(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            var oldImage = record.Dynamodb.OldImage;
            var newImage = record.Dynamodb.NewImage;

            var oldValue = int.Parse(oldImage["value"].N);
            var newValue = int.Parse(newImage["value"].N);

            var modificationTime = DateTime.UtcNow;
            modificationTime = modificationTime.AddTicks(-(modificationTime.Ticks % TimeSpan.TicksPerMillisecond));
            var formattedTime = modificationTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            var auditItem = new Dictionary<string, AttributeValue>
            {
                { "id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                { "itemKey", new AttributeValue { S = newImage["key"].S } },
                { "modificationTime", new AttributeValue { S = formattedTime } },
                { "updatedAttribute", new AttributeValue { S = "value" } },
                { "oldValue", new AttributeValue { N = oldValue.ToString() } },
                { "item.newValue", new AttributeValue 
                    { 
                        M = new Dictionary<string, AttributeValue>
                        {
                            { "key", new AttributeValue { S = newImage["key"].S } },
                            { "value", new AttributeValue { N = newValue.ToString() } }
                        }
                    } 
                }
            };

            await PutItemInAuditTable(auditItem, context);
            
            context.Logger.LogLine($"Audit item updated: {JsonSerializer.Serialize(auditItem)}");
        }

        private async Task PutItemInAuditTable(Dictionary<string, AttributeValue> auditItem, ILambdaContext context)
        {
            var putRequest = new PutItemRequest
            {
                TableName = _auditTableName,
                Item = auditItem
            };

            try
            {
                await _dynamoDbClient.PutItemAsync(putRequest);
                context.Logger.LogLine($"Created audit entry in table {_auditTableName}");
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error creating audit entry in table {_auditTableName}: {ex.Message}");
                context.Logger.LogLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
