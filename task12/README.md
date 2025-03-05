# task12

High level project overview - business value it brings, non-detailed technical overview.

### Notice
All the technical details described below are actual for the particular
version, or a range of versions of the software.
### Actual for versions: 1.0.0


## Lambdas descriptions

### Lambda `lambda-name`
Lambda feature overview.

### Required configuration
#### Environment variables
* environment_variable_name: description

#### Trigger event
```buildoutcfg
{
    "key": "value",
    "key1": "value1",
    "key2": "value3"
}
```
* key: [Required] description of key
* key1: description of key1

#### Expected response
```buildoutcfg
{
    "status": 200,
    "message": "Operation succeeded"
}
```
---

## Deployment from scratch
1. action 1 to deploy the software
2. action 2
...

	// "task12_api_ui": {
	// 	"resource_type": "swagger_ui",
	// 	"path_to_spec": "export/task12/8crsi0xksc_oas_v3.json",
	// 	"target_bucket": "api-ui-hoster",
	// 	"dependencies": [
	// 		{
	// 			"resource_name": "api-ui-hoster",
	// 			"resource_type": "s3_bucket"
	// 		}
	// 	]
	// }