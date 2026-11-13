using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    public class RootFolderValidator : PropertyValidator
    {
        private readonly IRootFolderService _rootFolderService;

        public RootFolderValidator(IRootFolderService rootFolderService)
        {
            _rootFolderService = rootFolderService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is already configured as a root folder";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            context.MessageFormatter.AppendArgument("path", context.PropertyValue?.ToString());

            if (context.PropertyValue == null)
            {
                return true;
            }

            var path = context.PropertyValue.ToString();
            var match = _rootFolderService.All().Find(root => root.Path.IsPathValid(PathValidationType.CurrentOs) && root.Path.PathEquals(path));

            if (match == null)
            {
                return true;
            }

            // Flat and custom movies are stored in the root. Movie-folder libraries are not.
            return _rootFolderService.GetFolderLayout(path) != MovieFolderLayout.MovieFolder;
        }
    }
}
