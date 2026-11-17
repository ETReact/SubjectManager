using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace SubjectManager
{
    [Export(typeof(IXrmToolBoxPlugin)),
     ExportMetadata("Name", "Subject Manager"),
     ExportMetadata("Description",
         "Bulk management of Dynamics 365 / Dataverse subjects: load the whole subject tree, then create, rename, " +
         "re-parent, show/hide and delete subjects in a grid, with a live tree preview of the result. " +
         "Changes are saved in one go with ExecuteMultiple."),
     ExportMetadata("SmallImageBase64", PluginImages.Small),
     ExportMetadata("BigImageBase64", PluginImages.Large),
     ExportMetadata("BackgroundColor", "Lavender"),
     ExportMetadata("PrimaryFontColor", "Black"),
     ExportMetadata("SecondaryFontColor", "Gray")]
    public class SubjectManagerPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new SubjectManagerControl();
        }
    }
}
