using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PixelForge.Core;
using PixelForge.Desktop;

internal static class WorkflowChecks
{
    private static int count;
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);count++;Console.WriteLine("PASS: "+message);}
    public static async Task Run(string existing,string root)
    {
        root=Path.GetFullPath(Path.Combine(root,Guid.NewGuid().ToString("N")[..8]));Directory.CreateDirectory(root);
        var profile=Path.Combine(root,"profile");var outside=Path.Combine(root,"user-file.txt");File.WriteAllText(outside,"keep");
        var first=new RenderSession(profile);string firstPath=first.DirectoryPath;File.WriteAllText(Path.Combine(firstPath,"generated.txt"),"temporary");first.Snapshot();
        using(var other=new RenderSession(profile)){Check(Directory.Exists(firstPath),"another live instance's leased session is never removed");Check(other.Retained.Count==0,"live session is not misreported as abandoned work");Check(!other.Unchanged(firstPath),"another instance's files are never treated as unchanged owned data for timing writes");}
        first.Dispose();Check(!Directory.Exists(firstPath),"unchanged application-owned session is cleaned on normal close");Check(File.ReadAllText(outside)=="keep","cleanup does not touch neighboring user files");
        var edited=new RenderSession(profile);string editedPath=edited.DirectoryPath;File.WriteAllText(Path.Combine(editedPath,"art.aseprite"),"original");edited.Snapshot();File.WriteAllText(Path.Combine(editedPath,"art.aseprite"),"saved manual edits");edited.Snapshot();edited.Dispose();
        Check(File.Exists(Path.Combine(editedPath,"art.aseprite")),"snapshot registration never acknowledges over a saved manual edit");
        using(var restarted=new RenderSession(profile))Check(Directory.Exists(editedPath)&&restarted.Retained.Contains(editedPath),"restart retains changed native work rather than sweeping it away");
        string unknown=Path.Combine(profile,"Temp",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(unknown);File.WriteAllText(Path.Combine(unknown,"user.txt"),"keep");
        using(var restarted=new RenderSession(profile))Check(Directory.Exists(unknown),"unmarked GUID directories are never deleted");
        using(var scoped=new RenderSession(Path.Combine(root,"scope")))
        {
            string a=Path.Combine(scoped.DirectoryPath,"render-a"),b=Path.Combine(scoped.DirectoryPath,"render-b");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(a,"asset.png"),"generated");scoped.Snapshot(a);File.WriteAllText(Path.Combine(a,"manual.png"),"user-created");File.WriteAllText(Path.Combine(b,"asset.png"),"later render");scoped.Snapshot(b);scoped.Dispose();
            Check(File.ReadAllText(Path.Combine(a,"manual.png"))=="user-created","registering a later render never claims unrelated new artwork for deletion");
        }
        using(var input=new RenderSession(Path.Combine(root,"input")))
        {
            string file=Path.Combine(input.DirectoryPath,"source.blend");File.WriteAllText(file,"imported source");input.Snapshot();input.ProtectInput(file);input.Dispose();Check(File.Exists(file),"explicitly imported source inside session storage is protected even when unchanged");
        }
        var abandoned=new RenderSession(profile);string abandonedPath=abandoned.DirectoryPath;File.WriteAllText(Path.Combine(abandonedPath,"old.png"),"registered");abandoned.Snapshot();
        typeof(RenderSession).GetField("lease",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(abandoned)!.GetType().GetMethod("Dispose",Type.EmptyTypes)!.Invoke(typeof(RenderSession).GetField("lease",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(abandoned),[]);
        using(var restarted=new RenderSession(profile))Check(!Directory.Exists(abandonedPath),"restart cleans a registered stale session after its process lease is lost");
        string processProfile=Path.Combine(root,"orphan-process");var arguments=new List<string>();
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))arguments.Add(typeof(WorkflowChecks).Assembly.Location);
        arguments.AddRange(["--session-orphan",processProfile]);await Toolchain.Run(Environment.ProcessPath!,arguments,Path.Combine(root,"orphan.log"));
        string orphanPath=Directory.GetDirectories(Path.Combine(processProfile,"Temp")).Single();using(var cleanup=new RenderSession(processProfile))Check(!Directory.Exists(orphanPath),"a separate process exiting without session disposal is safely cleaned on restart");
        var source=GeneratedAsset.Open(existing);
        string sourceHash=Hash(source.Aseprite);var nativePalette=await Palette.LoadNative(source.Aseprite,Toolchain.Discover().Aseprite,root);
        Check(nativePalette.Length>0&&Hash(source.Aseprite)==sourceHash,"real Aseprite palette import leaves source native artwork unchanged");
        Check(Directory.GetDirectories(root,"PixelForge-palette-*").Length==0,"native palette import removes only its own helper files");
        using(var session=new RenderSession(Path.Combine(root,"assets")))
        {
            string copy=Path.Combine(session.DirectoryPath,"render");Copy(source.Directory,copy);var temp=GeneratedAsset.Open(copy);session.Snapshot();
            var history=new RenderHistory(Path.Combine(root,"history.json"),session.IsManaged);var entry=history.AddCompleted(temp);
            Check(history.Recent.Count==1&&new RenderHistory(Path.Combine(root,"history.json"),session.IsManaged).Recent.Count==0,"temporary history is available in-session but never persisted");
            string pngHash=Hash(temp.Png);temp=AnimationTiming.Apply(temp,6);session.AcceptTimingChanges(temp.Directory);
            string hash=Hash(temp.Aseprite);var saved=session.Preserve(entry.Output);entry=history.Relocate(entry,saved);history.ToggleFavorite(entry);
            Check(File.Exists(saved.Aseprite)&&Hash(saved.Aseprite)==hash,"favorite preservation retains editable files without changing native artwork");
            Check(GeneratedAsset.Open(saved.Directory).Frames.Length==source.Frames.Length,"preserved manifest retains all frames after relocation");
            Check(GeneratedAsset.Open(saved.Directory).Frames.Sum(f=>f.DurationMs)==2000&&entry.Settings.Fps==6&&Hash(saved.Png)==pngHash,"preservation refreshes stale history timing from the latest saved asset without changing pixels");
            session.Dispose();Check(File.Exists(saved.Aseprite)&&new RenderHistory(Path.Combine(root,"history.json"),session.IsManaged).Favorites.Count==1,"favorite and native artwork survive session deletion and history restart");
            string exports=Path.Combine(root,"exports");Directory.CreateDirectory(exports);AssetExport.SaveSpritesheet(saved,Path.Combine(exports,"sheet.png"));Check(File.Exists(Path.Combine(exports,"sheet.json"))&&Directory.GetFiles(exports,"*.tmp").Length==0,"spritesheet export retains JSON and removes its own metadata scratch file");
        }
        using(var reloadedSession=new RenderSession(Path.Combine(root,"native-reload")))
        {
            string work=Path.Combine(reloadedSession.DirectoryPath,"render");Copy(source.Directory,work);var original=GeneratedAsset.Open(work);original=AnimationTiming.Apply(original,6);reloadedSession.Snapshot(work);
            string latest=Hash(original.Aseprite);var reloaded=await NativeEdits.Read(original,Toolchain.Discover());GeneratedAsset.Save(reloaded);reloadedSession.Snapshot(reloaded.Directory);
            var preserved=reloadedSession.Preserve(reloaded);reloadedSession.Dispose();
            Check(preserved.Aseprite.StartsWith(preserved.Directory+Path.DirectorySeparatorChar)&&Hash(preserved.Aseprite)==latest,"preserving reloaded frames includes the latest native document formerly outside their folder");
            Check(GeneratedAsset.Open(preserved.Directory).Frames.Length==12,"reloaded native asset remains complete after its temporary parent is cleaned");
        }
        var dirs=new DialogDirectories();foreach(string category in new[]{"models","settings","png","sheets","palettes","frames","native","assets"}){string path=Path.Combine(root,category);Directory.CreateDirectory(path);dirs.SelectedFile(category,Path.Combine(path,"file.json"));}
        Check(dirs.Locations.Values.Distinct().Count()==8,"all file-dialog categories retain separate directories");
        var persisted=JsonSerializer.Deserialize<DialogDirectories>(JsonSerializer.Serialize(dirs))!;Check(persisted.Initial("settings")==dirs.Initial("settings")&&persisted.Initial("png")!=persisted.Initial("sheets"),"directory categories round-trip independently");
        dirs.Locations["models"]=Path.Combine(root,"missing");Check(Directory.Exists(dirs.Initial("models",root)),"missing remembered directory falls back gracefully");
        Exception? failure=null;var thread=new Thread(()=>{try{Ui(root);}catch(Exception ex){failure=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw new Exception("Workflow UI checks",failure);
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks=count,profile,outside}));Console.WriteLine($"{count} workflow checks passed: {root}");await Task.CompletedTask;
    }
    private static void Ui(string root)
    {
        string? previous=Environment.GetEnvironmentVariable("PIXELFORGE_PROFILE");Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",Path.Combine(root,"ui"));
        try
        {
            var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;var window=new MainWindow();window.Show();
            T Find<T>(string name)=>(T)window.FindName(name);
            void Call(string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,args);
            Check(!Find<Expander>("ManageColors").IsExpanded&&Find<WrapPanel>("Swatches").Children.Count>0,"new install keeps swatches visible with Manage Colors collapsed");
            Find<Expander>("ManageColors").IsExpanded=true;Check(Find<WrapPanel>("Swatches").Children.Count==Palette.Default.Length,"expansion never changes palette");
            typeof(MainWindow).GetField("selectedSwatch",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,1);Call("CopyColor",window,new RoutedEventArgs());Check(Clipboard.GetText()==Palette.Default[1].Hex,"copy writes the selected HEX to the real Windows clipboard");
            void Respond(string label)=>window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                var dialog=app.Windows.OfType<Window>().Single(w=>w.Title=="Remove palette color"&&w.Owner==window&&w.IsVisible);
                static IEnumerable<Button> Buttons(DependencyObject parent){for(int n=0;n<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);n++){var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,n);if(child is Button b)yield return b;foreach(var inner in Buttons(child))yield return inner;}}
                Buttons(dialog).Single(b=>b.Content?.ToString()==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            int paletteCount=Find<WrapPanel>("Swatches").Children.Count;Respond("Cancel");Call("RemoveColor",window,new RoutedEventArgs());Check(Find<WrapPanel>("Swatches").Children.Count==paletteCount,"Cancel in actual color-removal dialog leaves palette unchanged");
            Respond("Remove");Call("RemoveColor",window,new RoutedEventArgs());Check(Find<WrapPanel>("Swatches").Children.Count==paletteCount-1,"Remove in actual confirmation removes only the selected swatch");
            foreach(var startupWindow in app.Windows.OfType<MainWindow>().Where(w=>w!=window).ToArray())startupWindow.Close();
            var hex=Find<TextBox>("OutlineColorInput");hex.Text="#112233";Check(Find<Button>("ApproveOutlineButton").Visibility==Visibility.Visible,"custom HEX is visibly outside strict palette");
            Find<CheckBox>("OutlineInput").IsChecked=true;bool rejected=false;try{Call("ReadSettings");}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){rejected=true;}Check(rejected,"strict generation rejects unapproved custom outline color");
            Call("ApproveOutlineColor",window,new RoutedEventArgs());Check(Find<Button>("ApproveOutlineButton").Visibility==Visibility.Collapsed,"explicit approval adds custom outline color");
            var settings=(AssetSettings)typeof(MainWindow).GetMethod("ReadSettings",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[])!;Check(settings.OutlineColor=="#112233"&&settings.Colors.Contains("#112233"),"approved custom outline survives settings read without substitution");
            hex.Text="invalid";Check(Find<TextBlock>("OutlineColorNotice").Text.Contains("valid"),"invalid HEX remains editable with validation feedback");hex.Text="#112233";
            Find<RadioButton>("GameGroundInput").IsChecked=true;Check(Find<RadioButton>("CheckerInput").IsChecked==false,"ground and checkerboard are mutually exclusive");
            Find<RadioButton>("CheckerInput").IsChecked=true;Check(Find<RadioButton>("GameGroundInput").IsChecked==false,"checkerboard cannot leave ground mode active");
            var ground=Find<CheckBox>("GroundShadowsInput");Find<NumericSlider>("GroundStrengthInput").Value=.7;Find<NumericSlider>("GroundSoftnessInput").Value=.45;ground.IsChecked=false;
            Check(((FrameworkElement)Find<NumericSlider>("GroundStrengthInput").Parent).Visibility==Visibility.Collapsed,"disabled ground settings collapse instead of disabling");ground.IsChecked=true;
            Check(Find<NumericSlider>("GroundStrengthInput").Value==.7&&Find<NumericSlider>("GroundSoftnessInput").Value==.45,"hidden controls retain exact values");
            var primary=Find<StackPanel>("PrimaryExports").Children.OfType<Button>().Select(b=>b.Content.ToString()).ToArray();Check(primary.SequenceEqual(new[]{"Save Current PNG","Save Spritesheet","Open in Aseprite"}),"bottom-left primary exports have exact order and labels");
            Check(Find<Button>("InspectRangeButton").Content.ToString()=="Inspect Animation Range","one inspection action remains next to frame/playback controls");
            var remembered=(DialogDirectories)typeof(MainWindow).GetField("directories",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            foreach(string category in new[]{"models","settings","png","sheets"})Call("RememberFile",category,Path.Combine(root,category,"test.json"));
            window.Close();var reopened=new MainWindow();Check(((Expander)reopened.FindName("ManageColors")).IsExpanded&&((TextBox)reopened.FindName("OutlineColorInput")).Text=="#112233","palette management and outline preferences survive restart");
            var reopenedDirs=(DialogDirectories)typeof(MainWindow).GetField("directories",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(reopened)!;Check(reopenedDirs.Locations.Count==4&&reopenedDirs.Initial("png")!=reopenedDirs.Initial("sheets"),"actual application preferences restore independent model/settings/PNG/sheet directories");
            reopened.Close();app.Shutdown();
        }
        finally{Environment.SetEnvironmentVariable("PIXELFORGE_PROFILE",previous);}
    }
    private static string Hash(string p)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
    private static void Copy(string source,string destination){Directory.CreateDirectory(destination);foreach(string file in Directory.GetFiles(source))File.Copy(file,Path.Combine(destination,Path.GetFileName(file)));foreach(string dir in Directory.GetDirectories(source))Copy(dir,Path.Combine(destination,Path.GetFileName(dir)));}
}
