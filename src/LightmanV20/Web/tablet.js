const sourceButtons=[...document.querySelectorAll('[data-source]')];
const test=document.getElementById('test');
const pause=document.getElementById('pause');
const stop=document.getElementById('stop');
const shows=document.getElementById('shows');
const schedulerState=document.getElementById('scheduler-state');
const schedulerDetail=document.getElementById('scheduler-detail');
const stateLabel=document.getElementById('state');
const active=document.getElementById('active');
const detail=document.getElementById('detail');
const lamp=document.getElementById('lamp');
let busy=false;
let showSignature='';

async function post(path,body={}){
  if(busy)return;
  busy=true;setDisabled(true);
  try{
    const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
    const payload=await response.json().catch(()=>({}));
    if(!response.ok)throw new Error(payload.error||'No se pudo aplicar');
    await refresh();
  }catch(error){showActionError(error.message)}finally{busy=false;setDisabled(false)}
}

function setDisabled(value){
  sourceButtons.forEach(b=>b.disabled=value);
  test.disabled=value;pause.disabled=value;stop.disabled=value;
  document.querySelectorAll('[data-show]').forEach(b=>b.disabled=value||b.dataset.enabled!=='true');
}

sourceButtons.forEach(button=>button.addEventListener('click',()=>post('/api/source',{source:button.dataset.source})));
test.addEventListener('click',()=>post('/api/test',{enabled:!test.classList.contains('active')}));
pause.addEventListener('click',()=>post('/api/show/pause'));
stop.addEventListener('click',()=>post('/api/show/stop'));

function renderShows(items,currentPlaylist){
  const signature=JSON.stringify(items||[]);
  if(signature!==showSignature){
    showSignature=signature;
    shows.replaceChildren(...(items||[]).map(item=>{
      const button=document.createElement('button');
      button.className='show'+(item.enabled?'':' pending');
      button.dataset.show=item.id;button.dataset.enabled=String(Boolean(item.enabled&&item.ready));
      button.disabled=!item.enabled||!item.ready;
      const title=document.createElement('strong');title.textContent=item.title;
      const note=document.createElement('em');note.textContent=item.ready?(item.note||'LISTO'):'ARCHIVO NO DISPONIBLE';
      button.append(title,note);
      button.addEventListener('click',()=>post('/api/show/play',{id:item.id}));
      return button;
    }));
  }
  document.querySelectorAll('[data-show]').forEach(button=>{
    const item=(items||[]).find(s=>s.id===button.dataset.show);
    button.classList.toggle('active',Boolean(currentPlaylist&&item&&item.playlist===currentPlaylist));
  });
}

function render(s){
  document.body.classList.remove('offline');
  sourceButtons.forEach(button=>button.classList.toggle('active',!s.generalTest&&button.dataset.source===s.source));
  test.classList.toggle('active',Boolean(s.generalTest));
  lamp.className='lamp '+(s.error?'error':s.signal?'on':'');
  stateLabel.textContent=s.error?'ERROR':s.generalTest?'PRUEBA ACTIVA':s.signal?'SEÑAL EN VIVO':'ESPERANDO SEÑAL';
  active.textContent=s.generalTest?'TEST GENERAL':s.sourceLabel;
  detail.textContent=s.error|| (s.generalTest?'Blanco, rojo, verde y azul · brillo limitado a 25%':
    s.signal?`${Math.round(s.packetRate||0)} paquetes/s · ${s.sender||'entrada activa'}`:'La salida física permanece en negro');

  const xs=s.scheduler||{};
  schedulerState.className='pill '+(!xs.connected?'error':String(xs.status).toLowerCase()==='playing'?'playing':'online');
  schedulerState.textContent=!xs.configured?'SIN CONFIGURAR':!xs.connected?'XSCHEDULE DESCONECTADO':String(xs.status||'LISTO').toUpperCase();
  schedulerDetail.textContent=xs.error|| (xs.playlist?`${xs.playlist}${xs.step?' · '+xs.step:''}`:'xSchedule listo para lanzar un show');
  pause.disabled=busy||!xs.connected||String(xs.status).toLowerCase()==='idle';
  stop.disabled=busy||!xs.connected||String(xs.status).toLowerCase()==='idle';
  renderShows(s.shows,xs.playlist);
}

function showActionError(message){
  schedulerState.className='pill error';schedulerState.textContent='NO APLICADO';schedulerDetail.textContent=message;
}
function showOffline(message){
  document.body.classList.add('offline');lamp.className='lamp error';stateLabel.textContent='SIN CONEXIÓN';active.textContent='V20';detail.textContent=message||'Verifica que V20 esté abierto';
}
async function refresh(){
  try{const response=await fetch('/api/state',{cache:'no-store'});if(!response.ok)throw new Error('Servidor no disponible');render(await response.json())}
  catch(error){showOffline(error.message)}
}
refresh();setInterval(refresh,700);
