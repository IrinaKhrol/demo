const AWS = require('aws-sdk');
const { v4: uuidv4 } = require('uuid');

const cognitoISP = new AWS.CognitoIdentityServiceProvider({
	region: process.env.region || 'eu-central-1',
});
const dynamodb = new AWS.DynamoDB.DocumentClient({
	region: process.env.region || 'eu-central-1',
});

const TABLES_TABLE = process.env.TABLES_TABLE;
const RESERVATIONS_TABLE = process.env.RESERVATIONS_TABLE;
const USER_POOL_ID = process.env.cup_id;
const CLIENT_ID = process.env.cup_client_id;

exports.handler = async (event) => {
	const { httpMethod, path, headers, body } = event;

	console.log('Received request:', { path, httpMethod, body, headers });

	try {
		if (path.startsWith('/tables/') && httpMethod === 'GET') {
			// const match = path.match(/^\/tables\/(\d+)$/);
			// if (!match || !match[1]) {
			// 	return {
			// 		statusCode: 400,
			// 		body: JSON.stringify({ message: 'Invalid table ID format' }),
			// 		headers: { 'Content-Type': 'application/json' },
			// 	};
			// }

			const splitPath = path.split('/');
			const tableId = splitPath[splitPath.length - 1];
			try {
				const table = await dynamodb
					.get({
						TableName: TABLES_TABLE,
						Key: { id: +tableId },
					})
					.promise();

				if (!table.Item) {
					return {
						statusCode: 400,
						body: JSON.stringify({ message: 'Table not found' }),
						headers: { 'Content-Type': 'application/json' },
					};
				}

				return {
					statusCode: 200,
					body: JSON.stringify(table.Item),
					headers: { 'Content-Type': 'application/json' },
				};
			} catch (err) {
				console.error('DynamoDB Error:', {
					code: err.code,
					message: err.message,
					stack: err.stack,
					tableId,
				});
				return {
					statusCode: 400,
					body: JSON.stringify({ message: 'Failed to retrieve table' }),
					headers: { 'Content-Type': 'application/json' },
				};
			}
		}

		switch (`${path}:${httpMethod}`) {
			case '/signup:POST': {
				const { firstName, lastName, email, password } = JSON.parse(
					body || '{}'
				);
				if (!validateSignupInput(firstName, lastName, email, password)) {
					return {
						statusCode: 400,
						body: JSON.stringify({ message: 'Invalid input' }),
						headers: { 'Content-Type': 'application/json' },
					};
				}

				try {
					const listUsersResponse = await cognitoISP
						.listUsers({
							UserPoolId: USER_POOL_ID,
							Filter: `email = "${email}"`,
						})
						.promise();

					if (listUsersResponse.Users && listUsersResponse.Users.length > 0) {
						return {
							statusCode: 200,
							body: JSON.stringify({ message: 'User already exists' }),
							headers: { 'Content-Type': 'application/json' },
						};
					}

					const params = {
						ClientId: CLIENT_ID,
						Username: email,
						Password: password,
						UserAttributes: [{ Name: 'email', Value: email }],
					};

					const data = await cognitoISP.signUp(params).promise();
					const confirmParams = {
						Username: email,
						UserPoolId: USER_POOL_ID,
					};

					const confirmedResult = await cognitoISP
						.adminConfirmSignUp(confirmParams)
						.promise();
					return {
						statusCode: 200,
						headers: { 'Content-Type': 'application/json' },
						body: JSON.stringify({ message: 'OK', data, confirmedResult }),
					};
				} catch (error) {
					console.error(error);
					return {
						statusCode: 500,
						headers: { 'Content-Type': 'application/json' },
						body: JSON.stringify({
							error: 'Signing up failed',
							details: error.message,
						}),
					};
				}
			}

			case '/signin:POST': {
				const { email, password } = JSON.parse(body || '{}');
				console.log('Signin attempt with:', { email, password });

				if (!email || !password) {
					console.log('Validation failed: Missing email or password');
					return {
						statusCode: 400,
						body: JSON.stringify({ message: 'Missing email or password' }),
						headers: { 'Content-Type': 'application/json' },
					};
				}

				try {
					const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
					if (!emailRegex.test(email)) {
						console.log('Validation failed: Invalid email format');
						return {
							statusCode: 400,
							body: JSON.stringify({ message: 'Invalid email format' }),
							headers: { 'Content-Type': 'application/json' },
						};
					}

					const passwordRegex =
						/^(?=.*[A-Za-z0-9])(?=.*[$%^*-_])[A-Za-z0-9$%^*-_]{12,}$/;
					if (!passwordRegex.test(password)) {
						console.log('Validation failed: Invalid password format');
						return {
							statusCode: 400,
							body: JSON.stringify({ message: 'Invalid password format' }),
							headers: { 'Content-Type': 'application/json' },
						};
					}

					console.log('Before adminInitiateAuth:', { email, password });
					const authResult = await cognitoISP
						.adminInitiateAuth({
							AuthFlow: 'ADMIN_USER_PASSWORD_AUTH',
							ClientId: CLIENT_ID,
							UserPoolId: USER_POOL_ID,
							AuthParameters: {
								USERNAME: email,
								PASSWORD: password,
							},
						})
						.promise();

					console.log('Auth result:', {
						authResult,
					});

					if (
						!authResult.AuthenticationResult ||
						!authResult.AuthenticationResult.AccessToken
					) {
						throw new Error(
							'Authentication result does not contain AccessToken'
						);
					}

					const AccessToken = authResult.AuthenticationResult.IdToken;

					return {
						statusCode: 200,
						body: JSON.stringify({
							accessToken: AccessToken,
							// allTokens: authResult.AuthenticationResult,
						}),
						headers: { 'Content-Type': 'application/json' },
					};
				} catch (error) {
					console.error('Cognito Auth Error:', {
						code: error.code,
						message: error.message,
						stack: error.stack,
						email,
						password,
					});
					if (error.code === 'NotAuthorizedException') {
						return {
							statusCode: 400,
							body: JSON.stringify({ message: 'Invalid email or password' }),
							headers: { 'Content-Type': 'application/json' },
						};
					}
					if (error.code === 'UserNotFoundException') {
						return {
							statusCode: 400,
							body: JSON.stringify({ message: 'User not found' }),
							headers: { 'Content-Type': 'application/json' },
						};
					}
					if (
						error.code === 'PasswordResetRequiredException' ||
						error.code === 'UserNotConfirmedException'
					) {
						return {
							statusCode: 400,
							body: JSON.stringify({
								message: 'User needs to confirm account or reset password',
							}),
							headers: { 'Content-Type': 'application/json' },
						};
					}
					if (error.code === 'InvalidParameterException') {
						return {
							statusCode: 400,
							body: JSON.stringify({
								message: 'Invalid request parameters: ' + error.message,
							}),
							headers: { 'Content-Type': 'application/json' },
						};
					}
					return {
						statusCode: 500,
						body: JSON.stringify({
							message: 'Failed to sign in: ' + error.message,
						}),
						headers: { 'Content-Type': 'application/json' },
					};
				}
			}

			case '/tables:GET': {
				const tables = await dynamodb
					.scan({
						TableName: TABLES_TABLE,
					})
					.promise();

				return {
					statusCode: 200,
					body: JSON.stringify({ tables: tables.Items }),
					headers: { 'Content-Type': 'application/json' },
				};
			}
			// case `tables/${tableId}:GET`: {
			// 	const table = await dynamodb
			// 		.get({
			// 			TableName: TABLES_TABLE,
			// 			Key: { id: +tableId },
			// 		})
			// 		.promise();

			// 	if (!table.Item) {
			// 		return {
			// 			statusCode: 400,
			// 			body: JSON.stringify({ message: 'Table not found' }),
			// 			headers: { 'Content-Type': 'application/json' },
			// 		};
			// 	}

			// 	return {
			// 		statusCode: 200,
			// 		body: JSON.stringify(table.Item),
			// 		headers: { 'Content-Type': 'application/json' },
			// 	};
			// }
			case '/tables:POST': {
				const { id, number, places, isVip, minOrder } = JSON.parse(
					body || '{}'
				);
				try {
					await dynamodb
						.put({
							TableName: TABLES_TABLE,
							Item: { id, number, places, isVip, minOrder },
						})
						.promise();
					return {
						statusCode: 200,
						body: JSON.stringify({ id }),
						headers: { 'Content-Type': 'application/json' },
					};
				} catch (err) {
					return {
						statusCode: 400,
						body: JSON.stringify({ message: 'Failed to create table' }),
						headers: { 'Content-Type': 'application/json' },
					};
				}
			}

			case '/reservations:POST': {
				const {
					tableNumber,
					clientName,
					phoneNumber,
					date,
					slotTimeStart,
					slotTimeEnd,
				} = JSON.parse(body || '{}');
				if (
					!validateReservationInput(
						tableNumber,
						date,
						slotTimeStart,
						slotTimeEnd
					)
				) {
					return {
						statusCode: 400,
						body: JSON.stringify({
							message:
								'There was an error in the request. Possible reasons include invalid input',
						}),
						headers: { 'Content-Type': 'application/json' },
					};
				}
				try {
					console.log('Scanning TABLES_TABLE for tableNumber:', tableNumber);
					const tablesScan = await dynamodb
						.scan({
							TableName: TABLES_TABLE,
							FilterExpression: '#num = :tableNumber',
							ExpressionAttributeNames: {
								'#num': 'number', // Экранируем зарезервированное слово 'number'
							},
							ExpressionAttributeValues: {
								':tableNumber': tableNumber, // Используем tableNumber как число
							},
						})
						.promise();
					console.log('Tables SCAN:', tablesScan);
					if (!tablesScan.Items || tablesScan.Items.length === 0) {
						console.log('Table not found for tableNumber:', tableNumber);
						return {
							statusCode: 400,
							body: JSON.stringify({
								message:
									'There was an error in the request. Possible reasons table not found for adding reservation',
							}),
							headers: { 'Content-Type': 'application/json' },
						};
					}

					console.log(
						'Scanning RESERVATIONS_TABLE for tableNumber:',
						tableNumber
					);
					console.log(
						'Querying RESERVATIONS_TABLE for tableNumber:',
						tableNumber
					);
					const reservationsQuery = await dynamodb
						.query({
							TableName: RESERVATIONS_TABLE,
							IndexName: 'TableNumberDateIndex', // Предполагаемый GSI на tableNumber и date
							KeyConditionExpression: 'tableNumber = :tableNumber',
							ExpressionAttributeValues: {
								':tableNumber': tableNumber, // Пробуем как строку, чтобы учесть возможный тип в таблице
							},
						})
						.promise();
					console.log('RESERVATION QUERY: ', reservationsQuery);
					if (reservationsQuery.Items && reservationsQuery.Items.length > 0) {
						console.log(
							'Reservation already exists for tableNumber:',
							tableNumber,
							'Items:',
							reservationsQuery.Items
						);
						return {
							statusCode: 400,
							body: JSON.stringify({
								message:
									'There was an error in the request. Possible reasons conflicting reservations.',
							}),
							headers: { 'Content-Type': 'application/json' },
						};
					}
					const reservationId = uuidv4();
					await dynamodb
						.put({
							TableName: RESERVATIONS_TABLE,
							Item: {
								id: reservationId,
								tableNumber: +tableNumber,
								clientName,
								phoneNumber,
								date,
								slotTimeStart,
								slotTimeEnd,
							},
						})
						.promise();

					return {
						statusCode: 200,
						body: JSON.stringify({ reservationId }),
						headers: { 'Content-Type': 'application/json' },
					};
				} catch (err) {
					console.error('DynamoDB Error:', {
						code: err.code,
						message: err.message,
						stack: err.stack,
						request: {
							tableNumber,
							clientName,
							phoneNumber,
							date,
							slotTimeStart,
							slotTimeEnd,
						},
					});
					return {
						statusCode: 500,
						body: JSON.stringify({ message: 'Failed to create reservation' }),
						headers: { 'Content-Type': 'application/json' },
					};
				}
			}

			case '/reservations:GET': {
				try {
					const reservations = await dynamodb
						.scan({
							TableName: RESERVATIONS_TABLE,
						})
						.promise();

					return {
						statusCode: 200,
						body: JSON.stringify({
							reservations: reservations.Items,
						}),
						headers: { 'Content-Type': 'application/json' },
					};
				} catch (err) {
					return {
						statusCode: 400,
						body: JSON.stringify({ message: 'Failed to get reservations' }),
						headers: { 'Content-Type': 'application/json' },
					};
				}
			}

			default: {
				return {
					statusCode: 404,
					body: JSON.stringify({ message: 'Unknown PATH' }),
					headers: { 'Content-Type': 'application/json' },
				};
			}
		}
	} catch (error) {
		console.error('Error:', error);
		return {
			statusCode: 500,
			body: JSON.stringify({ message: 'Internal Server Error' }),
			headers: { 'Content-Type': 'application/json' },
		};
	}
};

function validateSignupInput(firstName, lastName, email, password) {
	if (!firstName || !lastName || !email || !password) return false;

	const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
	if (!emailRegex.test(email)) return false;

	const passwordRegex =
		/^(?=.*[A-Za-z0-9])(?=.*[$%^*-_])[A-Za-z0-9$%^*-_]{12,}$/;
	return passwordRegex.test(password);
}
function validateReservationInput(
	tableNumber,
	date,
	slotTimeStart,
	slotTimeEnd
) {
	if (!tableNumber || !date || !slotTimeStart || !slotTimeEnd) return false;
	const dateRegex = /^\d{4}-\d{2}-\d{2}$/;
	if (!dateRegex.test(date)) return false;
	const timeRegex = /^([0-1][0-9]|2[0-3]):[0-5][0-9]$/;
	if (!timeRegex.test(slotTimeStart) || !timeRegex.test(slotTimeEnd))
		return false;
	return true;
}

function validateReserInput(
	tableNumber,
	clientName,
	phoneNumber,
	date,
	slotTimeStart,
	slotTimeEnd
) {
	// Проверка наличия всех обязательных полей
	if (
		!tableNumber ||
		!clientName ||
		!phoneNumber ||
		!date ||
		!slotTimeStart ||
		!slotTimeEnd
	) {
		return false;
	}

	// Проверка типа и формата tableNumber (должно быть число)
	if (
		typeof tableNumber !== 'number' ||
		isNaN(tableNumber) ||
		!Number.isInteger(tableNumber) ||
		tableNumber <= 0
	) {
		return false;
	}

	// Проверка типа и формата clientName (должно быть строка, не пустая)
	if (typeof clientName !== 'string' || clientName.trim() === '') {
		return false;
	}

	// Проверка типа и формата phoneNumber (должно быть строка, не пустая)
	if (typeof phoneNumber !== 'string' || phoneNumber.trim() === '') {
		return false;
	}

	// Проверка формата date (yyyy-MM-dd)
	const dateRegex = /^\d{4}-\d{2}-\d{2}$/;
	if (!dateRegex.test(date)) {
		return false;
	}

	// Проверка формата slotTimeStart и slotTimeEnd (HH:MM) и что slotTimeEnd > slotTimeStart
	const timeRegex = /^([0-1][0-9]|2[0-3]):[0-5][0-9]$/;
	if (!timeRegex.test(slotTimeStart) || !timeRegex.test(slotTimeEnd)) {
		return false;
	}

	// Разбор времени для сравнения
	const [startHours, startMinutes] = slotTimeStart.split(':').map(Number);
	const [endHours, endMinutes] = slotTimeEnd.split(':').map(Number);
	const startTimeInMinutes = startHours * 60 + startMinutes;
	const endTimeInMinutes = endHours * 60 + endMinutes;

	if (endTimeInMinutes <= startTimeInMinutes) {
		return false; // slotTimeEnd должен быть больше slotTimeStart
	}

	return true;
}
