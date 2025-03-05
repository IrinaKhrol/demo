package com.task12.repository;

import com.amazonaws.services.dynamodbv2.AmazonDynamoDB;
import com.amazonaws.services.dynamodbv2.AmazonDynamoDBClientBuilder;
import com.amazonaws.services.dynamodbv2.document.*;
import com.google.gson.Gson;
import com.task12.dto.TableDTO;
import java.util.LinkedList;
import java.util.Map;

public class TableRepository {
    private static final AmazonDynamoDB client = AmazonDynamoDBClientBuilder.defaultClient();
    private DynamoDB dynamoDB = new DynamoDB(client);

    public Map<String, Object>  handleListTablesRequest(String tableName) {
        var table = dynamoDB.getTable(tableName);
        var items = table.scan();
        var result = new LinkedList<Map<String, Object>>();

        var iterator = items.iterator();
        while (iterator.hasNext()) {
            Item item = iterator.next();
            result.add(item.asMap());
        }

        return Map.of("tables", result);
    }

    public Map<String, Object>  handleCreateTableRequest(String body, String tableName) {
        var request = new Gson().fromJson(body, TableDTO.class);
        var item = new Item();
        item.withInt("id", request.getId());
        item.withInt("number", request.getNumber());
        item.withInt("places", request.getPlaces());
        item.withBoolean("isVip", request.getIsVip());
        item.withInt("minOrder", request.getMinOrder());

        var table = dynamoDB.getTable(tableName);
        table.putItem(item);
        return Map.of("id", request.getId());
    }

    public Map<String, Object> getTable(Long tableId, String tableName) {
        Table table = dynamoDB.getTable(tableName);
        Item item = table.getItem("id", Long.valueOf(tableId));
        return item.asMap();
    }
}
