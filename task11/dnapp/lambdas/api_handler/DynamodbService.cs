using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace SimpleLambdaFunction;

public class DynamodbService
{
    private readonly AmazonDynamoDBClient _dbClient;
    private readonly DynamoDBContext _dynamoDbContext;

    public DynamodbService()
    {
        _dbClient = new AmazonDynamoDBClient();
        _dynamoDbContext = new DynamoDBContext(_dbClient);
    }

    public async Task<int> CreateTable(int id, int number, int places, bool isVip, int minOrder)
    {
        var tableName = Environment.GetEnvironmentVariable("tables_db_table_name");

        var tableRequest = new TableDynamoDB()
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

        await _dynamoDbContext.SaveAsync(tableRequest, config);

        return id;
    }

    public async Task<List<TableDTO>> GetTables()
    {
        var tableName = Environment.GetEnvironmentVariable("tables_db_table_name");

        var config = new DynamoDBOperationConfig
        {
            OverrideTableName = tableName
        };

        var tables = await _dynamoDbContext.ScanAsync<TableDynamoDB>(null, config).GetRemainingAsync();

        return tables.Select(entity =>
            new TableDTO
            {
                Id = Int32.Parse(entity.Id),
                Number = entity.Number,
                Places = entity.Places,
                IsVip = entity.IsVip,
                MinOrder = entity.MinOrder
            }
        ).ToList();
    }

    public async Task<string> CreateReservation(int tableNumber, string clientName, string date, string slotStart,
        string slotEnd, string phoneNumber)
    {
        var reservationsTableName = Environment.GetEnvironmentVariable("reservations_db_table_name");
        var tablesTableName = Environment.GetEnvironmentVariable("tables_db_table_name");

        // Config for reservations table
        var reservationsConfig = new DynamoDBOperationConfig
        {
            OverrideTableName = reservationsTableName
        };

        // Config for tables table
        var tablesConfig = new DynamoDBOperationConfig
        {
            OverrideTableName = tablesTableName
        };

        // Check if the table exists
        var tableExists = await TableExistsByNumber(tableNumber, tablesConfig);
        if (!tableExists)
        {
            throw new InvalidOperationException($"Table with number {tableNumber} does not exist.");
        }

        // Step 1: Scan for existing reservations for the same table and date
        var existingReservations = await GetReservationsForTableAndDate(tableNumber, date, reservationsConfig);

        // Step 2: Check for overlapping time slots
        foreach (var existing in existingReservations)
        {
            if (IsOverlapping(existing.SlotTimeStart, existing.SlotTimeEnd, slotStart, slotEnd))
            {
                throw new InvalidOperationException(
                    $"A reservation already exists for table {tableNumber} on {date} with overlapping time slot {existing.SlotTimeStart}-{existing.SlotTimeEnd}.");
            }
        }

        // Step 3: If no overlap, create and save the new reservation
        var reservation = new Reservations
        {
            ReservationId = Guid.NewGuid().ToString(),
            TableNumber = tableNumber,
            ClientName = clientName,
            PhoneNumber = phoneNumber,
            Date = date,
            SlotTimeStart = slotStart,
            SlotTimeEnd = slotEnd
        };

        await _dynamoDbContext.SaveAsync(reservation, reservationsConfig);

        return reservation.ReservationId;
    }

// Helper method to scan reservations for a specific table and date
    private async Task<List<Reservations>> GetReservationsForTableAndDate(int tableNumber, string date,
        DynamoDBOperationConfig config)
    {
        var scanConditions = new List<ScanCondition>
        {
            new("TableNumber", ScanOperator.Equal, tableNumber),
            new("Date", ScanOperator.Equal, date)
        };

        var search = _dynamoDbContext.ScanAsync<Reservations>(scanConditions, config);
        return await search.GetRemainingAsync();
    }

// Helper method to check if two time slots overlap
    private bool IsOverlapping(string existingStart, string existingEnd, string newStart, string newEnd)
    {
        var existingStartTime = TimeSpan.Parse(existingStart);
        var existingEndTime = TimeSpan.Parse(existingEnd);
        var newStartTime = TimeSpan.Parse(newStart);
        var newEndTime = TimeSpan.Parse(newEnd);

        // Overlap occurs if one slot starts before another ends
        return newStartTime < existingEndTime && existingStartTime < newEndTime;
    }

    public async Task<List<Reservations>> GetReservations()
    {
        var reservationsTableName = Environment.GetEnvironmentVariable("reservations_db_table_name");

        var config = new DynamoDBOperationConfig
        {
            OverrideTableName = reservationsTableName
        };

        var reservations = await _dynamoDbContext.ScanAsync<Reservations>(null, config).GetRemainingAsync();

        return reservations;
    }

    public async Task<TableDTO> GetTableById(string id)
    {
        var tableName = Environment.GetEnvironmentVariable("tables_db_table_name");

        var config = new DynamoDBOperationConfig
        {
            OverrideTableName = tableName
        };

        var entity = await _dynamoDbContext.LoadAsync<TableDynamoDB>(id, config);
        var result = new TableDTO
        {
            Id = Int32.Parse(entity.Id),
            Number = entity.Number,
            Places = entity.Places,
            IsVip = entity.IsVip,
            MinOrder = entity.MinOrder
        };
        return result;
    }
    
    private async Task<bool> TableExistsByNumber(int tableNumber, DynamoDBOperationConfig config)
    {
        var scanConditions = new List<ScanCondition>
        {
            new ScanCondition("Number", ScanOperator.Equal, tableNumber)
        };

        var search = _dynamoDbContext.ScanAsync<TableDynamoDB>(scanConditions, config);
        var results = await search.GetRemainingAsync();
        return results.Any();
    }
}