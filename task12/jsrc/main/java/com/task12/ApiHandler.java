package com.task12;

import com.amazonaws.services.lambda.runtime.Context;
import com.amazonaws.services.lambda.runtime.RequestHandler;
import com.amazonaws.services.lambda.runtime.events.APIGatewayProxyRequestEvent;
import com.amazonaws.services.lambda.runtime.events.APIGatewayProxyResponseEvent;
import com.google.gson.Gson;
import com.syndicate.deployment.annotations.lambda.LambdaHandler;
import com.syndicate.deployment.model.RetentionSetting;
import com.task12.repository.ReservationRepository;
import com.task12.repository.TableRepository;
import com.task12.service.AccessControlService;
import java.util.HashMap;
import java.util.Map;

@LambdaHandler(lambdaName = "api_handler",
        roleName = "api_handler-role",
        isPublishVersion = false,
        logsExpiration = RetentionSetting.SYNDICATE_ALIASES_SPECIFIED
)
public class ApiHandler implements RequestHandler<APIGatewayProxyRequestEvent, APIGatewayProxyResponseEvent> {
    private AccessControlService authService = new AccessControlService();
    private TableRepository tableRepository = new TableRepository();
    private ReservationRepository reservationRepository = new ReservationRepository();

    public APIGatewayProxyResponseEvent  handleRequest(APIGatewayProxyRequestEvent request, Context context) {
        String poolName = context.getFunctionName().replace("api_handler", "simple-booking-userpool");
        String tablesName = context.getFunctionName().replace("api_handler", "Tables");
        String reservationName = context.getFunctionName().replace("api_handler", "Reservations");
        var pathParameters = request.getPathParameters();
        var path = request.getPath();
        var httpMethod = request.getHttpMethod();

        Map<String, Object> resultMap = new HashMap<>();
        resultMap.put("statusCode", 400);

        try {

            System.out.println("Before case");
            Map<String, Object> result = null;
            if (path.startsWith("/tables")
                    && pathParameters != null
                    && pathParameters.get("tableId") != null) {
                var tableId = Long.valueOf(request.getPathParameters().get("tableId"));
                result = tableRepository.getTable(tableId, tablesName);
            } else {
                switch (path) {
                    case "/signup":
                        result = processSignup(request.getBody(), poolName);
                        break;
                    case "/signin":
                        result = processSignin(request.getBody(), poolName);
                        break;
                    case "/tables":
                        result = processTables(httpMethod, request.getBody(), tablesName);
                        break;
                    case "/reservations":
                        result = processReservations(httpMethod, request.getBody(), reservationName, tablesName);
                        break;
                    default:
                        throw new UnsupportedOperationException("Operation " +
                                path + " is not supported");
                }
            }
            return new APIGatewayProxyResponseEvent()
                    .withStatusCode(200)
                    .withBody(new Gson().toJson(result))
                    .withHeaders(Map.of(
                            "Content-Type", "application/json",
                            "Access-Control-Allow-Headers", "Content-Type,X-Amz-Date,Authorization,X-Api-Key,X-Amz-Security-Token",
                            "Access-Control-Allow-Origin", "*",
                            "Access-Control-Allow-Methods", "*",
                            "Accept-Version", "*"));
        } catch (Exception e) {
            System.out.println(e);
            resultMap.put("error", e.getMessage() + "\n" + e.getStackTrace());
            return new APIGatewayProxyResponseEvent()
                    .withStatusCode(400)
                    .withBody(new Gson().toJson(resultMap.toString()));
        }
    }

    private Map<String, Object> processTables(String httpMethod, String body, String name) {
        return httpMethod.equals("GET") ?
                tableRepository.handleListTablesRequest(name) :
                tableRepository.handleCreateTableRequest(body, name);
    }

    private Map<String, Object> processReservations(String httpMethod, String body, String rName, String tName) {
        return httpMethod.equals("GET") ?
                reservationRepository.handleListReservationsRequest(rName) :
                reservationRepository.handleCreateReservationRequest(body, rName, tName);
    }

    private Map<String, Object> processSignin(String body, String functionName) {
        return authService.signIn(body, functionName);
    }

    private Map<String, Object> processSignup(String body, String functionName) {
        return authService.signUp(body, functionName);
    }
}
