using VertexERP.Models;
using VertexERP.Services;
using VertexERP.Controllers;
using Microsoft.AspNetCore.Mvc;

static class ManagerDashboardChecks
{
    public static void Run()
    {
        int passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        Check(ManagerDashboardPresentation.Greeting(DateTimeOffset.Parse("2026-10-26T06:29:59Z"))=="Good Morning","11:59:59 India is morning");
        Check(ManagerDashboardPresentation.Greeting(DateTimeOffset.Parse("2026-10-26T06:30:00Z"))=="Good Afternoon","12:00 India switches to afternoon");
        Check(ManagerDashboardPresentation.Greeting(DateTimeOffset.Parse("2026-10-26T11:29:59Z"))=="Good Afternoon","16:59:59 India is afternoon");
        Check(ManagerDashboardPresentation.Greeting(DateTimeOffset.Parse("2026-10-26T11:30:00Z"))=="Good Evening","17:00 India switches to evening");
        Check(ManagerDashboardPresentation.IndiaDate(DateTime.Parse("2026-10-31T20:00:00Z").ToUniversalTime())==new DateOnly(2026,11,1),"India month boundary is independent of UTC server date");
        var today=new DateOnly(2026,10,26);
        WorkTask Task(string status,DateOnly due,string created)=>new(){Status=status,DueDate=due,CreatedAtUtc=DateTime.Parse(created).ToUniversalTime()};
        var tasks=new[]{Task("Completed",today.AddDays(-1),"2026-10-02T00:00:00Z"),Task("In Progress",today,"2026-10-26T00:00:00Z"),Task("To Do",today.AddDays(1),"2026-10-26T01:00:00Z"),Task("In Progress",today.AddDays(-1),"2026-10-25T00:00:00Z"),Task("To Do",today.AddDays(-1),"2026-09-30T00:00:00Z")};
        Check(ManagerDashboardPresentation.Workload(tasks,today,"month")==new ManagerWorkload(1,1,1,1),"Month chart uses mutually exclusive status segments");
        Check(ManagerDashboardPresentation.Workload(tasks,today,"all").Total==5,"All-time chart includes older tasks");
        Check(ManagerDashboardPresentation.Workload(tasks,today,"week").Total==2,"Week chart starts Monday in India");
        Check(ManagerDashboardPresentation.Workload([],today,"month").Total==0,"Empty chart remains zero without invented percentages");
        Check(ManagerDashboardPresentation.Workload(new[]{tasks[3]},today,"all")==new ManagerWorkload(0,0,0,1),"Overdue in-progress task counted only once");
        var leave=new LeaveRequest{Status="Pending",ApprovalLevel="Manager"};
        Check(ManagerDashboardPresentation.AwaitingAction(leave,true)&&!ManagerDashboardPresentation.AwaitingAction(leave,false),"Manager approvals exclude HR-stage requests");
        leave.ApprovalLevel="HR/Admin";
        Check(!ManagerDashboardPresentation.AwaitingAction(leave,true)&&ManagerDashboardPresentation.AwaitingAction(leave,false),"HR-stage request is not a pending manager action");
        leave.Status="Approved";
        Check(!ManagerDashboardPresentation.AwaitingAction(leave,false),"Completed approval is excluded");
        Check(ManagerDashboardPresentation.Initials("Sushil Kumar Tyagi")=="ST"&&ManagerDashboardPresentation.Initials(null)=="V","Missing/broken photos retain initials fallback");
        var model=new ManagerDashboardViewModel{IsManagerScope=true,LeaveRequests=new[]{new LeaveRequest{Status="Pending",ApprovalLevel="Manager"},new LeaveRequest{Status="Pending",ApprovalLevel="HR/Admin"}},ExpenseClaims=new[]{new ExpenseClaim{Status="Pending"}}};
        Check(model.PendingApprovals==2,"Pending approval metric combines actionable leaves and expenses");
        Check(typeof(MainController).GetMethods().Count(x=>x.Name=="ManagerNotificationCount")==1&&typeof(MainController).GetMethods().Count(x=>x.Name=="ManagerNotificationUpdates")==1,"Notification routes no longer have ambiguous GET overloads");
        Check(typeof(MainController).GetMethod("ManagerNotificationUpdates")!.IsDefined(typeof(HttpGetAttribute),true),"Notification updates are read-only");
        Console.WriteLine($"{passed} manager dashboard checks passed.");
    }
}
