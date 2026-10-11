namespace Cinerama.Domain.Entities;

/// <summary>Opinión de una persona sobre una película: puntuación de 1 a 5 y, opcionalmente, un comentario. Una por persona y película (RN-43).</summary>
public class Review
{
    public const byte MinRating = 1;
    public const byte MaxRating = 5;
    public const int MaxCommentLength = 500;

    public int Id { get; set; }
    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public byte Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
