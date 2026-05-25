using IA.API.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace IA.API.Controllers;

/// <summary>
/// Endpoint para listagem dos temas disponíveis na base de documentos.
/// </summary>
[ApiController]
[Route("/assistent/themes", Name = "Themes")]
public sealed class ThemesController : ControllerBase
{
    private readonly IRagRepository _repository;

    public ThemesController(IRagRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Lista os temas cadastrados para organização e consulta dos documentos.
    /// </summary>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna os temas em formato simplificado para consumo da API.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var themes = await _repository.ListThemesAsync(cancellationToken);
        return Ok(themes.Select(t => new { theme = t }));
    }
}
