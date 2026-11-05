using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;

internal static partial class Program
{
    static void ConcurrentCraterChecks()
    {
        var pending=new ConcurrentQueue<TaskCompletionSource<byte[]>>();
        using var synchronousSetup=new ManualResetEventSlim();
        int calls=0, active=0, peak=0;
        var f=Fixture(async(_,_)=> {
            var call=Interlocked.Increment(ref calls); var running=Interlocked.Increment(ref active);
            int prior; do { prior=peak; if(prior>=running) break; } while(Interlocked.CompareExchange(ref peak,running,prior)!=prior);
            if(call==1) synchronousSetup.Wait(TimeSpan.FromSeconds(5));
            var completion=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); pending.Enqueue(completion);
            try { return await completion.Task; } finally { Interlocked.Decrement(ref active); }
        });
        var products=(ObservableCollection<ExtractedProduct>)f.Products.ItemsSource;
        for(var i=1;i<25;i++) products.Add(new ExtractedProduct{ProductName="Product "+i,Price="$2"});
        var a=products[0];var b=products[1];var c=products[2];
        Button CardButton(ExtractedProduct p,string? label=null)
        {
            f.Products.ScrollIntoView(p);Pump();f.Products.UpdateLayout();
            return Children<Button>(f.Products).Single(x=>ReferenceEquals(x.Tag,p)&&(label==null?x.Name=="CraterItButton":x.Content?.ToString()==label));
        }
        void Click(ExtractedProduct p)=>CardButton(p).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TaskCompletionSource<byte[]> Next(){Until(()=>!pending.IsEmpty,"mock request is waiting");pending.TryDequeue(out var task);return task!;}
        Click(a);Until(()=>calls==1,"first card starts asynchronous generation");
        var heartbeat=false;f.Window.Dispatcher.BeginInvoke(new Action(()=>heartbeat=true));Pump();
        Check(heartbeat&&!synchronousSetup.IsSet,"dispatcher stays responsive during synchronous provider setup");synchronousSetup.Set();var first=Next();
        CardButton(a).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(calls==1&&!CardButton(a).IsEnabled,"duplicate same-product request blocked, only its Crater button disabled");
        Check(CardButton(b).IsEnabled&&f.Products.IsEnabled,"other product and offer list stay enabled");
        Click(b);Until(()=>calls==2,"second independent product generates concurrently");var second=Next();
        Click(c);Pump();Check(c.IsCraterGenerating&&c.CraterStatus=="Queued"&&calls==2,"third product queues without a third provider call");
        f.Products.ScrollIntoView(products.Last());Pump();
        Check(Children<ScrollViewer>(f.Products).Any(s=>s.VerticalOffset>0),"actual offer ScrollViewer scrolls while generation is pending");
        Check(!f.Window.Progress.IsRunning,"Crater does not use global progress panel");
        ((Button)f.Window.FindName("SaveDataButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(((TextBlock)f.Window.FindName("EditingContextText")).Text.Contains("Finish or cancel")&&!f.Window.Progress.IsRunning,"Save cannot race queued image assignment through global gate");
        CardButton(c,"Cancel image").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!c.IsCraterGenerating,"queued card cancellation completes without provider call");
        Check(calls==2&&c.ImageFilePath==null,"cancelled queued request never reaches provider");
        var d=products[3];Click(d);Check(calls==2&&d.CraterStatus=="Queued","another independent card waits for a request slot");
        first.SetResult(Jpeg());Until(()=>calls==3,"queued product starts when one request slot frees");var third=Next();
        Check(active==2,"slot handoff preserves two concurrent provider requests");
        third.SetResult(Jpeg());second.SetException(new IOException("mock failure"));
        Until(()=>!a.IsCraterGenerating&&!b.IsCraterGenerating&&!d.IsCraterGenerating,"independent cards complete and fail without blocking dispatcher");
        Check(d.CraterStatus.StartsWith("Complete")&&d.HasImage,"queued product completes and assigns its own artwork");
        Check(a.CraterStatus.StartsWith("Complete")&&File.Exists(a.ImageFilePath)&&a.ArtworkId!=null,"success archives and assigns captured product");
        Check(b.CraterStatus.Contains("mock failure")&&b.ImageFilePath==null&&CardButton(b).IsEnabled,"failure is per card and permits retry");
        Check(Children<TextBlock>(f.Products).Any(t=>t.Text==b.CraterStatus),"per-card feedback is bound in actual WPF template");
        Click(b);var retry=Next();retry.SetResult(Jpeg());Until(()=>!b.IsCraterGenerating,"failed product can retry successfully");
        Check(b.CraterStatus.StartsWith("Complete")&&File.Exists(b.ImageFilePath),"retry assigns only its product");
        ((Button)f.Window.FindName("SaveDataButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!f.Window.Progress.IsRunning,"Save proceeds after card jobs finish");
        Check(f.Window.Progress.State=="Success"&&products.Take(2).All(p=>p.HasImage&&p.ArtworkId!=null),"actual SQLite Save/reload preserves assigned artwork identity");
        a=products[0];var original=a.ImageFilePath;var before=Directory.GetFiles(Path.Combine(Root,"ui-images"),"*.jpg").Length;
        Click(a);var cancelled=Next();CardButton(a,"Cancel image").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(a.IsCraterGenerating,"uncooperative active provider keeps slot and duplicate guard until it returns");
        cancelled.SetResult(Jpeg(180));Until(()=>!a.IsCraterGenerating,"late cancelled response drains safely");
        Check(a.ImageFilePath==original&&a.CraterStatus.StartsWith("Cancelled"),"active cancellation preserves current image and explains uncertain remote cancellation");
        Check(Directory.GetFiles(Path.Combine(Root,"ui-images"),"*.jpg").Length==before+1,"late completed cancelled output remains archived");
        var old=products[1];var oldImage=old.ImageFilePath;Click(old);var stale=Next();
        var pageList=(ListBox)f.Window.FindName("PageList");var page=new FlyerPage{FileName="new.pdf",FullPath=Path.Combine(Root,"missing.pdf")};pageList.Items.Add(page);pageList.SelectedItem=page;Pump();
        var replacement=new ExtractedProduct{ProductName="Replacement",Price="$4"};products.Add(replacement);stale.SetResult(Jpeg());
        Until(()=>!old.IsCraterGenerating,"selection change cancels pending card operation");
        Check(old.ImageFilePath==oldImage&&replacement.ImageFilePath==null,"stale response assigns neither old selection nor replacement product");
        Check(peak==2,"mock provider peak concurrency is exactly two");
        Click(replacement);var closing=Next();f.Window.Close();closing.SetResult(Jpeg());Until(()=>!replacement.IsCraterGenerating,"window close cancels and drains late generation");
        Check(replacement.ImageFilePath==null,"closed window never assigns late response");
    }
}
