namespace Homewatch.Contracts.Dtos.UserDtos
{
    public enum UpdateUserEmailResult
    {
        Success = 0,
        UserNotFound = 1,
        InvalidEmail = 2,
        EmailInUse = 3,
    }
}
