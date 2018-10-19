#addin "Cake.XdtTransform"
#addin "Cake.Powershell"
#addin "Cake.Http"
#addin "Cake.Json"
#addin "Cake.Azure"
#addin "Newtonsoft.Json"


#load "local:?path=CakeScripts/helper-methods.cake"


var target = Argument<string>("Target", "Default");
var configuration = new Configuration();
var cakeConsole = new CakeConsole();
var configJsonFile = "cake-config.json";

/*===============================================
================ MAIN TASKS =====================
===============================================*/

Setup(context =>
{
	cakeConsole.ForegroundColor = ConsoleColor.Yellow;
	PrintHeader(ConsoleColor.DarkGreen);
	
    var configFile = new FilePath(configJsonFile);
    configuration = DeserializeJsonFromFile<Configuration>(configFile);
});

Task("Default")
.WithCriteria(configuration != null)
.IsDependentOn("Build")
.IsDependentOn("Azure-Upload")
.IsDependentOn("Azure-Deploy");

Task("Run-Prerequisites")
.WithCriteria(configuration != null)
.IsDependentOn("Capture-UserData")
.IsDependentOn("Prepare-Environments");

Task("Build")
.WithCriteria(configuration != null)
.IsDependentOn("Clean")
.IsDependentOn("Publish-All-Projects")
//.IsDependentOn("Apply-Xml-Transform")
//.IsDependentOn("Publish-Transforms")
.IsDependentOn("Publish-xConnect-Project")
	//.IsDependentOn("Deploy-EXM-Campaigns")
	//.IsDependentOn("Deploy-Marketing-Definitions")
	//.IsDependentOn("Rebuild-Core-Index")
	//.IsDependentOn("Rebuild-Master-Index")
	//.IsDependentOn("Rebuild-Web-Index")
.IsDependentOn("Publish-YML")
.IsDependentOn("Publish-Azure-Transforms")
.IsDependentOn("Package-Build");

Task("Azure-Upload")
.WithCriteria(configuration != null)
.IsDependentOn("Azure-Upload-Packages");

Task("Azure-Deploy")
.WithCriteria(configuration != null)
.IsDependentOn("Azure-Site-Deploy");

/*===============================================
================= SUB TASKS =====================
===============================================*/

Task("Clean").Does(() => {

	if (DirectoryExists($"{configuration.DeployFolder}\\assets\\HabitatHome"))
    {
        CleanDirectories($"{configuration.DeployFolder}\\assets\\HabitatHome");
    }

	if (DirectoryExists($"{configuration.DeployFolder}\\Website"))
    {
        CleanDirectories($"{configuration.DeployFolder}\\Website");
    }

	if (DirectoryExists($"{configuration.DeployFolder}\\assets\\Xconnect"))
    {
        CleanDirectories($"{configuration.DeployFolder}\\assets\\Xconnect");
    }

	if (DirectoryExists($"{configuration.DeployFolder}\\assets\\Data Exchange Framework\\WDPWorkFolder"))
    {
        CleanDirectories($"{configuration.DeployFolder}\\assets\\Data Exchange Framework\\WDPWorkFolder");
    }
	
    CleanDirectories($"{configuration.SourceFolder}/**/obj");
    CleanDirectories($"{configuration.SourceFolder}/**/bin");
});

Task("Capture-UserData").Does(() => {
	StartPowershellFile ($"{configuration.ProjectFolder}\\Azure PaaS\\HelperScripts\\AzureUser-Config-Capture.ps1", args =>
        {
            args.AppendQuoted($"{configuration.ProjectFolder}\\Azure PaaS\\cake-config.json");
        });
		});

Task("Prepare-Environments").Does(() => {
	StartPowershellFile ($"{configuration.ProjectFolder}\\Azure PaaS\\HelperScripts\\Env-Prep.ps1", args =>
        {
            args.AppendQuoted($"{configuration.ProjectFolder}\\Azure PaaS\\cake-config.json");
        });
		});        

Task("Publish-All-Projects")
.IsDependentOn("Build-Solution")
.IsDependentOn("Publish-Foundation-Projects")
.IsDependentOn("Publish-Feature-Projects")
.IsDependentOn("Publish-Project-Projects");


Task("Build-Solution").Does(() => {
    MSBuild(configuration.SolutionFile, cfg => InitializeMSBuildSettings(cfg));
});

Task("Publish-Foundation-Projects").Does(() => {
    PublishProjects(configuration.FoundationSrcFolder, $"{configuration.DeployFolder}\\Website\\HabitatHome");
});

Task("Publish-Feature-Projects").Does(() => {
    PublishProjects(configuration.FeatureSrcFolder, $"{configuration.DeployFolder}\\Website\\HabitatHome");
});

Task("Publish-Project-Projects").Does(() => {
    var common = $"{configuration.ProjectSrcFolder}\\Common";
    var habitat = $"{configuration.ProjectSrcFolder}\\Habitat";
    var habitatHome = $"{configuration.ProjectSrcFolder}\\HabitatHome";

    PublishProjects(common, $"{configuration.DeployFolder}\\Website\\HabitatHome");
    PublishProjects(habitat, $"{configuration.DeployFolder}\\Website\\HabitatHome");
    PublishProjects(habitatHome, $"{configuration.DeployFolder}\\Website\\HabitatHome");
});

Task("Publish-xConnect-Project").Does(() => {
    var xConnectProject = $"{configuration.ProjectSrcFolder}\\xConnect";
	var xConnectDeployFolder = $"{configuration.DeployFolder}\\Website\\xConnect";
	
    PublishProjects(xConnectProject, xConnectDeployFolder);
});

Task("Apply-Xml-Transform").Does(() => {
    var layers = new string[] { configuration.FoundationSrcFolder, configuration.FeatureSrcFolder, configuration.ProjectSrcFolder};

    foreach(var layer in layers)
    {
        Transform(layer);
    }
});

Task("Publish-Transforms").Does(() => {
    var layers = new string[] { configuration.FoundationSrcFolder, configuration.FeatureSrcFolder, configuration.ProjectSrcFolder};
    var destination = $@"{configuration.DeployFolder}\Website\HabitatHome\temp\transforms";

    CreateFolder(destination);

    try
    {
        var files = new List<string>();
        foreach(var layer in layers)
        {
            var xdtFiles = GetTransformFiles(layer).Select(x => x.FullPath).ToList();
            files.AddRange(xdtFiles);
        }   

        CopyFiles(files, destination, preserveFolderStructure: true);
    }
    catch (System.Exception ex)
    {
        WriteError(ex.Message);
    }
});

Task("Publish-Azure-Transforms").Does(()=>{

       var codeFoldersFilter = $@"{configuration.ProjectFolder}\**\code";
       var destination = $@"{configuration.DeployFolder}\Website\HabitatHome";  

       if (!DirectoryExists(destination))
       {
             CreateFolder(destination);
       }

        try
        {    
            var projectDirectories = GetDirectories(codeFoldersFilter);
         
            foreach (var directory in projectDirectories)
            {
                var xdtFiles = GetTransformFiles(directory.FullPath).ToList();

                foreach (var file in xdtFiles)
                {
                    var relativeFilePath = file.FullPath;

                    var indexToTrimFrom = file.FullPath.IndexOf("/code/", StringComparison.InvariantCultureIgnoreCase);

                    if (indexToTrimFrom > -1)
                    {
                        relativeFilePath = file.FullPath.Substring(indexToTrimFrom + 5);
                    }
                    else
                    {
                        relativeFilePath = file.GetFilename().FullPath;
                    }

                    var destinationFilePath = new FilePath ($@"{destination}{relativeFilePath}");

                    if (!DirectoryExists(destinationFilePath.GetDirectory().FullPath))
                    {
                         CreateFolder(destinationFilePath.GetDirectory().FullPath);
                    }                   
                   
                    CopyFile(file.FullPath, destinationFilePath.FullPath);                   
                }  
            }   

        }
        catch (System.Exception ex)
        {
            WriteError(ex.Message);
        }

});

Task("Deploy-EXM-Campaigns").Does(() => {
    var url = $"{configuration.InstanceUrl}utilities/deployemailcampaigns.aspx?apiKey={configuration.MessageStatisticsApiKey}";
    string responseBody = HttpGet(url);

    Information(responseBody);
});

Task("Deploy-Marketing-Definitions").Does(() => {
    var url = $"{configuration.InstanceUrl}utilities/deploymarketingdefinitions.aspx?apiKey={configuration.MarketingDefinitionsApiKey}";
    string responseBody = HttpGet(url);

    Information(responseBody);
});

Task("Rebuild-Core-Index").Does(() => {
    RebuildIndex("sitecore_core_index");
});

Task("Rebuild-Master-Index").Does(() => {
    RebuildIndex("sitecore_master_index");
});

Task("Rebuild-Web-Index").Does(() => {
    RebuildIndex("sitecore_web_index");
});

Task("Publish-YML").Does(() => {

	var serializationFilesFilter = $@"{configuration.ProjectFolder}\**\*.yml";
    var destination = $@"{configuration.DeployFolder}\Website\HabitatHome\App_Data";

    if (!DirectoryExists(destination))
    {
        CreateFolder(destination);
    }

    try
    {
        var files = GetFiles(serializationFilesFilter).Select(x=>x.FullPath).ToList();

        CopyFiles(files , destination, preserveFolderStructure: true);
    }
    catch (System.Exception ex)
    {
        WriteError(ex.Message);
    }

	/* StartPowershellFile (($"{configuration.ProjectFolder}\\Azure PaaS\\HelperScripts\\Publish-YML.ps1"), args =>
        {
            args.AppendQuoted($"{configuration.ProjectFolder}\\Azure PaaS\\cake-config.json");
        });*/
		});

Task("Package-Build")
.IsDependentOn("Generate-HabitatUpdatePackages")
.IsDependentOn("ConvertTo-SCWDPs");

Task("Generate-HabitatUpdatePackages").Does(() => {
	StartPowershellFile ($"{configuration.ProjectFolder}\\Azure PaaS\\HelperScripts\\Generate-HabitatUpdatePackages.ps1", args =>
        {
            args.AppendQuoted($"{configuration.ProjectFolder}\\Azure PaaS\\cake-config.json");
        });
		});

Task("ConvertTo-SCWDPs").Does(() => {
	StartPowershellFile ($"{configuration.ProjectFolder}\\Azure PaaS\\HelperScripts\\ConvertTo-SCWDPs.ps1", args =>
        {
            args.AppendQuoted($"{configuration.ProjectFolder}\\Azure PaaS\\cake-config.json");
        });
		});

Task("Azure-Upload-Packages").Does(() => {
	StartPowershellFile ($"{configuration.ProjectFolder}\\Azure PaaS\\HelperScripts\\Upload-Packages.ps1", args =>
        {
            args.AppendQuoted($"{configuration.ProjectFolder}\\Azure PaaS\\cake-config.json");
        });
		});

Task("Azure-Site-Deploy").Does(() => {
	StartPowershellFile ($"{configuration.ProjectFolder}\\Azure PaaS\\HelperScripts\\Azure-Deploy.ps1", args =>
        {
            args.AppendQuoted($"{configuration.ProjectFolder}\\Azure PaaS\\cake-config.json");
        });
		});

RunTarget(target);
