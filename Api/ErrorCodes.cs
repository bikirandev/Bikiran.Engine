namespace Bikiran.Engine.Api;

/// <summary>
/// Standardized error codes used across all engine API responses.
/// </summary>
public static class ErrorCodes
{
    public const string DefinitionNotFound = "DEFINITION_NOT_FOUND";
    public const string InvalidFlowJson = "INVALID_FLOW_JSON";
    public const string ParameterRequired = "PARAMETER_REQUIRED";
    public const string ParameterTypeMismatch = "PARAMETER_TYPE_MISMATCH";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string DefinitionInactive = "DEFINITION_INACTIVE";
    public const string TriggerFailed = "TRIGGER_FAILED";
    public const string ValidationOnly = "VALIDATION_RESULT";
    public const string VersionNotFound = "VERSION_NOT_FOUND";
    public const string ImportFailed = "IMPORT_FAILED";
    public const string ScheduleNotFound = "SCHEDULE_NOT_FOUND";
    public const string RunNotFound = "RUN_NOT_FOUND";
    public const string KeyRequired = "KEY_REQUIRED";
}
