using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;

static class EmployeeCategoryChecks
{
    public static void Run()
    {
        int count=0;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
        foreach(var model in new object[]{new HrAddEmployeeViewModel(),new EmployeeFormViewModel(),new Employee()})
        {
            foreach(var category in new string?[]{null,"",EmployeeCategories.BlueCollar,EmployeeCategories.WhiteCollar,"Grey Collar","blue collar"," White Collar "})
            {
                var errors=new List<ValidationResult>();
                var valid=Validator.TryValidateProperty(category,new ValidationContext(model){MemberName="EmployeeCategory"},errors);
                Check(valid==(category is null or "" or "Blue Collar" or "White Collar"),model.GetType().Name+" validates category: "+(category??"unassigned"));
            }
        }
        Check(new Employee().EmployeeCategory==null,"Legacy/imported employees default to unassigned rather than an inferred category");
        var employee=new Employee{Id=42,EmployeeCode="EMP42",EmployeeCategory="Blue Collar"};
        var toForm=typeof(EmployeeController).GetMethod("ToForm",BindingFlags.NonPublic|BindingFlags.Static)!;
        var edit=(EmployeeFormViewModel)toForm.Invoke(null,new object[]{employee})!;
        Check(edit.EmployeeCategory=="Blue Collar","Edit form restores the saved category");
        edit.EmployeeCategory="White Collar";
        var apply=typeof(EmployeeController).GetMethod("ApplyForm",BindingFlags.NonPublic|BindingFlags.Static)!;
        apply.Invoke(null,new object[]{employee,edit,true});
        Check(employee.EmployeeCategory=="White Collar"&&employee.Id==42&&employee.EmployeeCode=="EMP42","HR edit changes category while preserving employee identity");
        edit.EmployeeCategory=null;apply.Invoke(null,new object[]{employee,edit,true});
        Check(employee.EmployeeCategory==null,"HR can leave an employee unassigned without guessing a category");
        Check(typeof(EmployeeController).GetCustomAttribute<AuthorizeAttribute>()!.Roles=="Admin,HR","Employee editing remains restricted to Admin and HR");
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        var field=db.Model.FindEntityType(typeof(Employee))!.FindProperty("EmployeeCategory")!;
        Check(field.IsNullable&&field.GetMaxLength()==20,"Category is persisted as a bounded optional database field");
        var script=db.GetService<IMigrator>().GenerateScript("20261026100000_SetOfficeStart0945","AddEmployeeCategory");
        Check(script.Contains("EmployeeCategory")&&script.Contains("Blue Collar")&&script.Contains("White Collar")&&script.Contains("CHECK"),"Migration adds category and database validation without connecting to a database");
        Check(!script.Contains("UPDATE \"Employees\""),"Migration does not assign or rewrite existing employee categories");
        Console.WriteLine($"{count} employee category checks passed.");
    }
}
