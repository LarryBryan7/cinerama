namespace Cinerama.Domain.Entities;

public class Role
{
    public byte Id { get; set; }
    public string Name { get; set; } = null!;
}

/// <summary>Ids y nombres fijos de los roles. Los datos semilla de la migración los usan.</summary>
public static class Roles
{
    public const byte AdminId = 1;
    public const byte CustomerId = 2;

    public const string Admin = "Admin";
    public const string Customer = "Customer";
}
