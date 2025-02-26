using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System;
using System.Collections.Generic;
using System.Text.Json;

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
            _auditTableName = Environment.GetEnvironmentVariable("TARGET_TABLE") ?? "Audit";
        }

        public void FunctionHandler(DynamoDBEvent dynamoEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Processing {dynamoEvent.Records.Count} records...");

            foreach (var record in dynamoEvent.Records)
            {
                try
                {
                    if (record.EventName == "INSERT")
                    {
                        HandleInsert(record, context);
                    }
                    else if (record.EventName == "MODIFY")
                    {
                        HandleModify(record, context);
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"Error processing record: {ex.Message}");
                }
            }
        }

        private void HandleInsert(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            var newImage = record.Dynamodb.NewImage;
            var auditItem = new Dictionary<string, AttributeValue>
            {
                { "id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                { "itemKey", newImage["key"] },
                { "modificationTime", new AttributeValue { S = DateTime.UtcNow.ToString("o") } },
                { "newValue", new AttributeValue { S = JsonSerializer.Serialize(new {
                    key = newImage["key"].S,
                    value = int.Parse(newImage["value"].N)
                })} }
            };

            PutItemInAuditTable(auditItem, context);
        }

        private void HandleModify(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            var oldImage = record.Dynamodb.OldImage;
            var newImage = record.Dynamodb.NewImage;
            
            var oldValue = int.Parse(oldImage["value"].N);
            var newValue = int.Parse(newImage["value"].N);

            var auditItem = new Dictionary<string, AttributeValue>
            {
                { "id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                { "itemKey", newImage["key"] },
                { "modificationTime", new AttributeValue { S = DateTime.UtcNow.ToString("o") } },
                { "updatedAttribute", new AttributeValue { S = "value" } },
                { "oldValue", new AttributeValue { N = oldValue.ToString() } },
                { "newValue", new AttributeValue { N = newValue.ToString() } }
            };

            PutItemInAuditTable(auditItem, context);
        }

        private void PutItemInAuditTable(Dictionary<string, AttributeValue> auditItem, ILambdaContext context)
        {
            var putRequest = new PutItemRequest
            {
                TableName = _auditTableName,
                Item = auditItem
            };

            try
            {
                _dynamoDbClient.PutItemAsync(putRequest).Wait();
                context.Logger.LogLine($"Created audit entry in table {_auditTableName}");
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error creating audit entry: {ex.Message}");
            }
        }
    }
}
