using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using Radarr.Http;

namespace Radarr.Api.V3.Movies;

[V3ApiController("movie")]
public class MovieFolderController : Controller
{
    private readonly IMovieService _movieService;
    private readonly IBuildMoviePaths _moviePathBuilder;
    private readonly IBuildFileNames _fileNameBuilder;

    public MovieFolderController(IMovieService movieService, IBuildMoviePaths moviePathBuilder, IBuildFileNames fileNameBuilder)
    {
        _movieService = movieService;
        _moviePathBuilder = moviePathBuilder;
        _fileNameBuilder = fileNameBuilder;
    }

    [HttpGet("{id:int}/folder")]
    [Produces("application/json")]
    public Ok<MovieFolderResource> GetFolder([FromRoute] int id, [FromQuery] string rootFolderPath = null)
    {
        var movie = _movieService.GetMovie(id);

        if (rootFolderPath.IsNotNullOrWhiteSpace())
        {
            movie.RootFolderPath = rootFolderPath;
        }

        // Flat movies have no folder of their own: Movie.Path is the root.
        // Custom movies use the rendered format, which may itself be empty.
        var folder = _fileNameBuilder.GetMovieFolder(movie);

        if (movie.RootFolderPath.IsNotNullOrWhiteSpace())
        {
            var path = _moviePathBuilder.BuildPath(movie, false);
            folder = movie.RootFolderPath.IsParentPath(path)
                ? movie.RootFolderPath.GetRelativePath(path)
                : string.Empty;
        }

        return TypedResults.Ok(new MovieFolderResource
        {
            Folder = folder
        });
    }
}
