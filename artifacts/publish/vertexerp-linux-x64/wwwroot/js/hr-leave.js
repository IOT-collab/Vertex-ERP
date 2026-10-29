(() => {
 const form=document.getElementById('hr-leave-form');if(!form)return;
 const employee=document.getElementById('hl-employee'),type=document.getElementById('hl-type'),from=document.getElementById('hl-from'),to=document.getElementById('hl-to');
 function update(){const half=type.value==='Half Day',short=type.value==='Short Leave';document.getElementById('hl-session').hidden=!half;form.querySelectorAll('[data-hl-short]').forEach(label=>{label.hidden=!short;label.querySelector('input').required=short;});document.getElementById('hl-facility').hidden=!(half||short);to.readOnly=half||short;if(half||short)to.value=from.value;const option=employee.selectedOptions[0];document.getElementById('hl-manager').textContent=employee.value?'Reporting manager: '+option.dataset.manager:'Select an employee to see the reporting manager.';}
 [employee,type,from].forEach(input=>input.addEventListener('change',update));update();
})();
