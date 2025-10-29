using Microsoft.Extensions.DependencyInjection;

public sealed class GroupResource(string name) : Resource(name) { }

public static class GroupingExtensions
{
    public static IResourceBuilder<GroupResource> AddGroup(this IDistributedApplicationBuilder builder, [ResourceName] string name)
{
    var groupInitialSnapshot = new CustomResourceSnapshot
    {
        ResourceType = "Group",
        Properties = [],
        State = new ResourceStateSnapshot(KnownResourceStates.Running, KnownResourceStateStyles.Success),
        StartTimeStamp = DateTime.UtcNow
    };

    var resource = new GroupResource(name);
    var resourceBuilder = builder.AddResource(resource)
        .WithInitialState(groupInitialSnapshot);

    builder.Eventing.Subscribe<ResourceReadyEvent>(resource, async (evt, _) =>
    {
        var rns = evt.Services.GetRequiredService<ResourceNotificationService>();
        
        foreach (var builderResource in builder.Resources)
        {
            if (builderResource.TryGetAnnotationsOfType<ResourceRelationshipAnnotation>(out var annotations)
                && annotations.Any(a => a.Resource == resource && a.Type == "Parent"))
            {
                await rns.PublishUpdateAsync(builderResource, previous => 
                    previous with
                    {
                        Properties = [.. previous.Properties,
                            new ResourcePropertySnapshot("resource.parentName", resource.Name)],
                    });
            }
        }
    });

    return resourceBuilder;
}
}
