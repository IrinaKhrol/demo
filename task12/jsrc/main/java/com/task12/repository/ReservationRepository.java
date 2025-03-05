package com.task12.repository;

import com.amazonaws.services.dynamodbv2.AmazonDynamoDB;
import com.amazonaws.services.dynamodbv2.AmazonDynamoDBClientBuilder;
import com.amazonaws.services.dynamodbv2.document.*;
import com.google.gson.Gson;
import com.task12.dto.ReservationDTO;
import java.math.BigDecimal;
import java.time.LocalTime;
import java.util.Iterator;
import java.util.LinkedList;
import java.util.List;
import java.util.Map;
import java.util.UUID;

public class ReservationRepository {
    TableRepository tableRepository = new TableRepository();

    private static final AmazonDynamoDB client = AmazonDynamoDBClientBuilder.defaultClient();
    private DynamoDB dynamoDB = new DynamoDB(client);

    public Map<String, Object> handleListReservationsRequest(String tableName) {
        Table table = dynamoDB.getTable(tableName);
        ItemCollection<ScanOutcome> items = table.scan();
        var result = new LinkedList<Map<String, Object>>();

        Iterator<Item> iterator = items.iterator();
        while (iterator.hasNext()) {
            Item item = iterator.next();
            System.out.println("Retrieved tables item:" + item.toJSONPretty());

            result.add(item.asMap());
        }

        return Map.of("reservations", result);
    }

    public Map<String, Object>  handleCreateReservationRequest(String body, String rName, String tName) {
        var request = new Gson().fromJson(body, ReservationDTO.class);
        var uuid = UUID.randomUUID();
        checkTableExist(request.getTableNumber(), tName);
        checkOverlapping(request, rName);
        var item = new Item();
        item.withString("id", uuid.toString());
        item.withInt("tableNumber", request.getTableNumber());
        item.withString("clientName", request.getClientName());
        item.withString("phoneNumber", request.getPhoneNumber());
        item.withString("date", request.getDate());
        item.withString("slotTimeStart", request.getSlotTimeStart());
        item.withString("slotTimeEnd", request.getSlotTimeEnd());

        var table = dynamoDB.getTable(rName);
        table.putItem(item);

        return Map.of("reservationId", uuid.toString());
    }

    private void checkTableExist(int tableNumber, String tName) {
        var tables = (List<Map<String, Object>>) tableRepository.handleListTablesRequest(tName).get("tables");

        var tableExists = tables.stream()
                .anyMatch(map -> ((BigDecimal) map.get("number")).intValue() == tableNumber);

        if (!tableExists) {
            throw new RuntimeException("Table does not exist");
        }
    }

    private void checkOverlapping(ReservationDTO request, String rName) {
        var checkStart = LocalTime.parse(request.getSlotTimeStart());
        var checkEnd = LocalTime.parse(request.getSlotTimeEnd());
        var reservations = (List<Map<String, Object>>) handleListReservationsRequest(rName).get("reservations");

        reservations.stream()
                .filter(map -> ((BigDecimal) map.get("tableNumber")).intValue() == request.getTableNumber())
                .filter(map -> map.get("date").equals(request.getDate()))
                .forEach(reservation -> {
                    LocalTime slotStart = LocalTime.parse(reservation.get("slotTimeStart").toString());
                    LocalTime slotEnd = LocalTime.parse(reservation.get("slotTimeEnd").toString());
                    if(checkEnd.isAfter(slotStart) && checkStart.isBefore(slotEnd)) {
                        throw new RuntimeException("Time overlap");
                    }
                });
    }
}