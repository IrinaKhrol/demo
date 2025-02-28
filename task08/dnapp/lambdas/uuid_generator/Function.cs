using Amazon.Lambda.Core;
using Amazon.S3;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Annotations;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SimpleLambdaFunction
{
    public class Function
    {
        private readonly IAmazonS3 _s3Client;

        public Function()
        {
            _s3Client = new AmazonS3Client(); 
        }

        [LambdaFunction]
        public async Task<string> FunctionHandler(ILambdaContext context)
        {
            string executionTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            var uuids = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                uuids.Add(Guid.NewGuid().ToString());
            }

            var data = new { ids = uuids };
            string jsonData = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

            string bucketName = Environment.GetEnvironmentVariable("TARGET_BUCKET") ?? "uuid-storage";

            string fileName = executionTime;

            var request = new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = bucketName,
                Key = fileName,
                ContentBody = jsonData,
                ContentType = "application/json"
            };

            try
            {
                await _s3Client.PutObjectAsync(request);
                context.Logger.LogLine($"Successfully stored UUIDs in {bucketName}/{fileName}");
                return $"UUIDs generated and stored successfully at {executionTime}";
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error storing UUIDs: {ex.Message}");
                throw;
            }
        }
    }
}
