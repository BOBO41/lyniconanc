﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Diagnostics;
using EnvDTE;
using System.Reflection;
using System.IO;

namespace LyniconANC.Tools
{
    // Windows PowerShell assembly.

    // Declare the class as a cmdlet and specify and 
    // appropriate verb and noun for the cmdlet name.
    [Cmdlet(VerbsData.Initialize, "LyniconProject")]
    public class InitializeLyniconProjectCommand : Cmdlet
    {
        public Action<string> Send { get; set; }
        public bool AllOK { get; set; }

        public InitializeLyniconProjectCommand()
        {
            Send = msg => SendMessage(new Tools.MessageEventArgs(MessageType.Output, msg));
        }

        private bool SendOutput(string msg)
        {
            Send(msg);
            return false;
        }

        protected override void ProcessRecord()
        {
            var fileModel = ProjectContextLoader.GetItemFileModel(SendMessage, "Startup.cs");
            UpdateStartup(fileModel);
        }

        public bool UpdateStartup(FileModel fileModel)
        {
            bool added;
            bool found;
            bool succeeded = true;

            fileModel.ToTop();

            if (fileModel.FindLineContains(".AddLynicon("))
                return true;

            fileModel.ToTop();
            
            // Update constructor

            found = fileModel.FindLineContains("Startup(");
            found = found && fileModel.FindEndOfMethod();
            if (found)
            {
                added = fileModel.InsertUniqueLineWithIndent("env.ConfigureLog4Net(\"log4net.xml\");");
                if (added)
                    SendOutput("Added log4net configuration");
            }
            else
                succeeded = SendOutput("Failed to add log4net configuration");
                
            // Update ConfigureServices method

            fileModel.ToTop();

            found = fileModel.FindLineContains(".AddMvc(");
            if (found)
            {
                bool doneAddLynOpts = fileModel.ReplaceText(".AddMvc()", ".AddMvc(options => options.AddLyniconOptions())");
                if (!doneAddLynOpts)
                    doneAddLynOpts = fileModel.ReplaceText(".AddMvc(options => options.", ".AddMvc(options => options.AddLyniconOptions()");
                if (!doneAddLynOpts)
                    succeeded = SendOutput("Failed to add setup for Lynicon options in MVC");
                else
                    SendOutput("Added Lynicon options in MVC");
                bool addedAppPart = fileModel.InsertTextAfterMatchingBracket(".AddMvc(", ".AddApplicationPart(typeof(LyniconSystem).Assembly)");
                if (!addedAppPart)
                    succeeded = SendOutput("Failed to add adding application part for Lynicon");
                else
                    SendOutput("Added adding application part for Lynicon");
            }
            found = found && fileModel.FindEndOfMethod();
            if (found)
            {
                added = fileModel.InsertUniqueLineWithIndent("services.AddIdentity<User, IdentityRole>()");
                if (added)
                {
                    fileModel.InsertLineWithIndent("\t.AddDefaultTokenProviders();");
                    SendOutput("Added services for ASP.Net Identity");
                }
                else
                {
                    SendOutput("The startup file already is initialising ASP.Net Identity - if you can, remove this or if not possible, use LyniconANC.Identity package and adapt it for your existing configuration");
                    return false;
                }

                fileModel.InsertLineWithIndent("");

                if (EnsureCalledAndHasOptionsMethod(fileModel, "services.AddAuthorization(", "AddLyniconAuthorization()"))
                {
                    SendOutput("Added Lynicon Authorization");
                    fileModel.InsertLineWithIndent("");
                } 
                else
                    SendOutput("Failed to add Lynicon Authorization");

                fileModel.InsertLineWithIndent("services.AddLynicon(options =>");
                fileModel.InsertLineWithIndent("\toptions.UseConfiguration(Configuration.GetSection(\"Lynicon:Core\"))");
                fileModel.InsertLineWithIndent("\t.UseModule<CoreModule>())");
                fileModel.InsertLineWithIndent(".AddLyniconIdentity();", true);
            }
            else
                succeeded = SendOutput("Failed to add ASP.Net Identity services setup");

            // Update Configure method

            fileModel.ToTop();

            found = fileModel.FindLineContains("void Configure(");

            if (!found)
            {
                SendOutput("Failed to set up Configure method: can't find Configure method");
                return false;
            }

            string lifeVar = "life";
            if (fileModel.CurrentLine.Contains("IApplicationLifetime"))
                lifeVar = fileModel.CurrentLine.After("IApplicationLifetime").UpTo(",").UpTo(")").Trim();
            else
            {
                found = fileModel.ReplaceText(")", ", IApplicationLifetime life)");
                if (found)
                    SendOutput("Added IApplicationLifetime parameter to Configure()");
                else
                    SendOutput("Failed to add IApplicationLifetime parameter to Configure()");
            }

            int currLine = fileModel.LineNum;
            found = fileModel.FindLineContains(".UseIdentity()");
            if (!found)
                fileModel.LineNum = currLine;

            if (!fileModel.FindLineContains(".UseMvc("))
            {
                SendOutput("Failed to set up Configure method: no .UseMvc() call");
                return false;
            }

            if (!found)
            {
                fileModel.Jump(-1);
                fileModel.InsertUniqueLineWithIndent("app.UseIdentity();");
                fileModel.InsertLineWithIndent("");
                SendOutput("Added UseIdentity()");
            }

            fileModel.InsertUniqueLineWithIndent("app.ConstructLynicon();");
            SendOutput("Added ConstructLynicon()");

            fileModel.FindLineContains(".UseMvc(");
            found = fileModel.ReplaceText(".UseMvc()", ".UseMvc(routes => { routes.MapLyniconRoutes(); })");
            if (!found)
                found = fileModel.ReplaceText(".UseMvc(routes => routes.)", ".UserMvc(routes => routes.MapLyniconRoutes().");
            if (!found)
            {
                fileModel.FindLineIs("{");
                fileModel.InsertUniqueLineWithIndent("routes.MapLyniconRoute();", true);
            }
            if (found)
                SendOutput("Added mapping Lynicon routes");
            else
                SendOutput("Failed to add mapping Lynicon routes");

            found = fileModel.FindPrevLineContains("void Configure(");
            found = found && fileModel.FindEndOfMethod();
            if (found)
            {
                fileModel.InsertLineWithIndent("");
                found = fileModel.InsertUniqueLineWithIndent("app.InitialiseLynicon(" + lifeVar + ");");
            }

            if (found)
                SendOutput("Added initialising Lynicon");
            else
                SendOutput("Failed to add initialising Lynicon");

            return succeeded;
        }

        private bool EnsureCalledAndHasOptionsMethod(FileModel fileModel, string methodLhs, string optionsMethod)
        {
            int currLineNum = fileModel.LineNum;
            bool found = fileModel.FindLineContains(methodLhs);
            if (!found)
            {
                fileModel.ToTop();
                found = fileModel.FindLineContains(methodLhs);
            }
            if (found)
            {
                bool done = fileModel.ReplaceText(methodLhs + ")", methodLhs + "options => options." + optionsMethod + ")");
                if (!done)
                    done = fileModel.ReplaceText(methodLhs + "options => options.", methodLhs + "options => options." + optionsMethod);
                return done;
            }
            else
            {
                fileModel.LineNum = currLineNum;
                fileModel.InsertLineWithIndent(methodLhs + "options => options." + optionsMethod + ")");
            }

            return true;
        }

        public void SendMessage(MessageEventArgs e)
        {
            MessageHandler.Handle(this, e);
        }
    }
}
