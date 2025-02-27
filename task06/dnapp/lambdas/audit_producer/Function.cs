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
    // Модель для записи аудита
    public class AuditEntry
    {
        public string id { get; set; }
        public string itemKey { get; set; }
        public string modificationTime { get; set; }
        public string updatedAttribute { get; set; }
        public int? oldValue { get; set; }
        public dynamic item { get; set; }
    }

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
                }
            }
        }

        private async Task HandleInsert(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            var newImage = record.Dynamodb.NewImage;
            var key = newImage["key"].S;
            var value = int.Parse(newImage["value"].N);

            var auditEntry = new AuditEntry
            {
                id = Guid.NewGuid().ToString(),
                itemKey = key,
                modificationTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                item = new { 
                    newValue = new { 
                        key, 
                        value 
                    } 
                }
            };

            await InsertAuditRecordAsync(auditEntry, context);
        }

        private async Task HandleModify(DynamoDBEvent.DynamodbStreamRecord record, ILambdaContext context)
        {
            var oldImage = record.Dynamodb.OldImage;
            var newImage = record.Dynamodb.NewImage;
            
            var key = newImage["key"].S;
            var oldValue = int.Parse(oldImage["value"].N);
            var value = int.Parse(newImage["value"].N);

            var auditEntry = new AuditEntry
            {
                id = Guid.NewGuid().ToString(),
                itemKey = key,
                modificationTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                updatedAttribute = "value",
                oldValue = oldValue,
                item = new { 
                    newValue = new { 
                        key, 
                        value 
                    } 
                }
            };

            await InsertAuditRecordAsync(auditEntry, context);
        }

        private async Task InsertAuditRecordAsync(AuditEntry auditEntry, ILambdaContext context)
        {
            // Конвертируем объект в словарь атрибутов для DynamoDB
            var auditItem = new Dictionary<string, AttributeValue>();
            
            // Добавляем базовые поля
            auditItem["id"] = new AttributeValue { S = auditEntry.id };
            auditItem["itemKey"] = new AttributeValue { S = auditEntry.itemKey };
            auditItem["modificationTime"] = new AttributeValue { S = auditEntry.modificationTime };
            
            // Добавляем опциональные поля
            if (!string.IsNullOrEmpty(auditEntry.updatedAttribute))
            {
                auditItem["updatedAttribute"] = new AttributeValue { S = auditEntry.updatedAttribute };
            }
            
            if (auditEntry.oldValue.HasValue)
            {
                auditItem["oldValue"] = new AttributeValue { N = auditEntry.oldValue.Value.ToString() };
            }
            
            // Сериализуем вложенное свойство item в атрибуты
            if (auditEntry.item != null)
            {
                // Преобразуем объект в строку JSON, затем обратно в словарь для создания структуры атрибутов
                string itemJson = JsonSerializer.Serialize(auditEntry.item);
                var dynamicItem = JsonSerializer.Deserialize<Dictionary<string, object>>(itemJson);
                
                if (dynamicItem.ContainsKey("newValue"))
                {
                    string newValueJson = JsonSerializer.Serialize(dynamicItem["newValue"]);
                    var newValueObj = JsonSerializer.Deserialize<Dictionary<string, object>>(newValueJson);
                    
                    auditItem["item"] = new AttributeValue
                    {
                        M = new Dictionary<string, AttributeValue>
                        {
                            { "newValue", new AttributeValue
                                {
                                    M = new Dictionary<string, AttributeValue>
                                    {
                                        { "key", new AttributeValue { S = newValueObj["key"].ToString() } },
                                        { "value", new AttributeValue { N = newValueObj["value"].ToString() } }
                                    }
                                }
                            }
                        }
                    };
                }
            }
            
            var putRequest = new PutItemRequest
            {
                TableName = _auditTableName,
                Item = auditItem
            };

            try
            {
                await _dynamoDbClient.PutItemAsync(putRequest);
                context.Logger.LogLine($"Created audit entry in table {_auditTableName} with ID {auditEntry.id}");
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error creating audit entry in table {_auditTableName}: {ex.Message}");
            }
        }
    }
}
