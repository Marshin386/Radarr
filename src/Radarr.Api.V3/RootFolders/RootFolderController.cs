using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using Radarr.Http;
using Radarr.Http.Extensions;
using Radarr.Http.REST;
using Radarr.Http.REST.Attributes;

namespace Radarr.Api.V3.RootFolders
{
    [V3ApiController]
    public class RootFolderController : RestControllerWithSignalR<RootFolderResource, RootFolder>
    {
        private readonly IRootFolderService _rootFolderService;

        public RootFolderController(IRootFolderService rootFolderService,
                                IBroadcastSignalRMessage signalRBroadcaster,
                                PathExistsValidator pathExistsValidator,
                                MappedNetworkDriveValidator mappedNetworkDriveValidator,
                                RecycleBinValidator recycleBinValidator,
                                StartupFolderValidator startupFolderValidator,
                                SystemFolderValidator systemFolderValidator,
                                FolderWritableValidator folderWritableValidator)
        : base(signalRBroadcaster)
        {
            _rootFolderService = rootFolderService;

            // A movie may live in a flat or custom root, but two root folders may not share a path.
            // The same id is the folder being edited, so changing its layout must not be rejected.
            SharedValidator.RuleFor(c => c.Path)
                .Cascade(CascadeMode.Stop)
                .IsValidPath()
                .Must((resource, path) => !_rootFolderService.All().Any(root =>
                    root.Id != resource.Id &&
                    root.Path.IsPathValid(PathValidationType.CurrentOs) &&
                    root.Path.PathEquals(path)))
                .WithMessage("Path '{PropertyValue}' is already configured as a root folder")
                .SetValidator(mappedNetworkDriveValidator)
                .SetValidator(startupFolderValidator)
                .SetValidator(recycleBinValidator)
                .SetValidator(pathExistsValidator)
                .SetValidator(systemFolderValidator)
                .SetValidator(folderWritableValidator);
        }

        protected override RootFolderResource GetResourceById(int id)
        {
            var timeout = Request?.GetBooleanQueryParameter("timeout", true) ?? true;

            return _rootFolderService.Get(id, timeout).ToResource();
        }

        [RestPostById]
        [Consumes("application/json")]
        public Results<Created<RootFolderResource>, NotFound> CreateRootFolder([FromBody] RootFolderResource rootFolderResource)
        {
            var model = rootFolderResource.ToModel();

            return Created(_rootFolderService.Add(model).Id);
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<List<RootFolderResource>> GetRootFolders()
        {
            return TypedResults.Ok(_rootFolderService.AllWithUnmappedFolders().ToResource());
        }

        [RestPutById]
        [Consumes("application/json")]
        public Results<Accepted<RootFolderResource>, NotFound> UpdateRootFolder([FromBody] RootFolderResource rootFolderResource)
        {
            var existing = _rootFolderService.Get(rootFolderResource.Id, true);

            existing.FolderLayout = rootFolderResource.FolderLayout;

            return Accepted(_rootFolderService.Update(existing).Id);
        }

        [RestDeleteById]
        public Ok DeleteFolder(int id)
        {
            _rootFolderService.Remove(id);

            return TypedResults.Ok();
        }
    }
}
