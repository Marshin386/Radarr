using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    public class MovieAncestorValidator : PropertyValidator
    {
        private readonly IMovieService _movieService;
        private readonly IRootFolderService _rootFolderService;

        public MovieAncestorValidator(IMovieService movieService, IRootFolderService rootFolderService)
        {
            _movieService = movieService;
            _rootFolderService = rootFolderService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is an ancestor of an existing movie";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            var path = context.PropertyValue.ToString();
            context.MessageFormatter.AppendArgument("path", path);

            // Group folders may contain both loose files and per-movie subfolders.
            if (_rootFolderService.GetFolderLayout(path) != MovieFolderLayout.MovieFolder)
            {
                return true;
            }

            return !_movieService.AllMoviePaths().Any(s => path.IsParentPath(s.Value));
        }
    }
}
