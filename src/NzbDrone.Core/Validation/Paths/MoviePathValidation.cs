using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    public class MoviePathValidator : PropertyValidator
    {
        private readonly IMovieService _moviesService;
        private readonly IRootFolderService _rootFolderService;

        public MoviePathValidator(IMovieService moviesService, IRootFolderService rootFolderService)
        {
            _moviesService = moviesService;
            _rootFolderService = rootFolderService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is already configured for an existing movie";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            dynamic instance = context.ParentContext.InstanceToValidate;
            var instanceId = (int)instance.Id;

            var path = context.PropertyValue.ToString();
            context.MessageFormatter.AppendArgument("path", path);

            // Flat and custom roots intentionally store several movies at the same folder.
            if (_rootFolderService.GetFolderLayout(path) != MovieFolderLayout.MovieFolder)
            {
                return true;
            }

            // Skip the path for this movie and any invalid paths
            return !_moviesService.AllMoviePaths().Any(s => s.Key != instanceId &&
                                                            s.Value.IsPathValid(PathValidationType.CurrentOs) &&
                                                            s.Value.PathEquals(path));
        }
    }
}
