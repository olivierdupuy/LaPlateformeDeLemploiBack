using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.DTOs;

public class MessageCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Candidature inconnue.")]
    public int ApplicationId { get; set; }

    // Un message de messagerie, pas un roman : la borne evite qu'un envoi
    // unique remplisse la conversation et la rende illisible pour les deux
    // parties. Le balisage y est tolere — on ecrit « a < b » sans arriere-pensee.
    [Required(ErrorMessage = "Écrivez votre message.")]
    [StringLength(Limites.Texte, MinimumLength = 1, ErrorMessage = "Le message ne peut pas dépasser 20 000 caractères.")]
    public string Content { get; set; } = string.Empty;
}
