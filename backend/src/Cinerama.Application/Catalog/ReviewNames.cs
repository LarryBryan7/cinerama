namespace Cinerama.Application.Catalog;

public static class ReviewNames
{
    /// <summary>RN-44: nombre y la inicial del último apellido ("Lucía Vargas Ríos" → "Lucía R."). Nunca el nombre completo.</summary>
    public static string Public(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Anónimo",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}
