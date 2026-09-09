const sourceButtons=[...document.querySelectorAll('[data-source]')];
const test=document.getElementById('test');
const pause=document.getElementById('pause');
const stop=document.getElementById('stop');
const shows=document.getElementById('shows');
const schedulerState=document.getElementById('scheduler-state');
const schedulerDetail=document.getElementById('scheduler-detail');
const importSummary=document.getElementById('import-summary');
const importTitle=document.getElementById('import-title');
const importState=document.getElementById('import-state');
const importAdded=document.getElementById('import-added');
const importIncomplete=document.getElementById('import-incomplete');
const importErrors=document.getElementById('import-errors');
const importMessage=document.getElementById('import-message');
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

function renderShows(items,currentPlaylist,sync){
  const syncBlocksAuto=Boolean(sync&&(sync.skippedBusy||sync.restartRequired||sync.waitingForXSchedule||countOf(sync.errors)>0));
  const invalidPlaylists=new Set((sync?.invalidCandidates||[]).map(issue=>String(issue.playlist||'')));
  const signature=JSON.stringify([items||[],syncBlocksAuto,sync?.skippedBusy,sync?.restartRequired,
    sync?.waitingForXSchedule,[...invalidPlaylists]]);
  if(signature!==showSignature){
    showSignature=signature;
    shows.replaceChildren(...(items||[]).map(item=>{
      const invalidCandidate=Boolean(item.autoDiscovered&&invalidPlaylists.has(String(item.playlist||'')));
      const waitingForSchedule=Boolean(item.autoDiscovered&&
        (item.scheduleReady===false||(item.scheduleReady==null&&(syncBlocksAuto||invalidCandidate))));
      const playable=Boolean(item.enabled&&item.ready&&!waitingForSchedule);
      const button=document.createElement('button');
      button.className='show'+(playable?'':' pending')+(item.autoDiscovered?' auto-imported':'');
      button.dataset.show=item.id;button.dataset.enabled=String(playable);
      button.disabled=!playable;
      const title=document.createElement('strong');title.textContent=item.title;
      const note=document.createElement('em');
      note.textContent=waitingForSchedule?(invalidCandidate?'PENDIENTE: ARCHIVOS DEL SHOW INVÁLIDOS':
        sync.skippedBusy?'PENDIENTE: XSCHEDULE ESTÁ OCUPADO':
        sync.waitingForXSchedule?'PENDIENTE: ABRE XSCHEDULE Y REINICIA V20':
        sync.restartRequired?'PENDIENTE: REINICIA XSCHEDULE Y V20':'PENDIENTE: ERROR DE XSCHEDULE'):
        !item.ready?'ARCHIVO NO DISPONIBLE':(item.note||'LISTO');
      button.append(title,note);
      if(item.autoDiscovered){
        const origin=document.createElement('i');origin.textContent='AUTO · INTERNO';button.append(origin);
      }
      button.addEventListener('click',()=>post('/api/show/play',{id:item.id}));
      return button;
    }));
  }
  document.querySelectorAll('[data-show]').forEach(button=>{
    const item=(items||[]).find(s=>s.id===button.dataset.show);
    button.classList.toggle('active',Boolean(currentPlaylist&&item&&item.playlist===currentPlaylist));
  });
}

function countOf(value){
  if(Array.isArray(value))return value.length;
  const count=Number(value);return Number.isFinite(count)&&count>0?Math.trunc(count):0;
}

function renderAutoImport(report,sync,items){
  if(!report&&!sync){importSummary.hidden=true;return}
  report=report||{};sync=sync||{};
  const added=countOf(report.added);
  const incomplete=Math.max(countOf(report.incomplete),countOf(sync.invalidCandidates));
  const unresolvedAuto=(items||[]).filter(item=>item.autoDiscovered&&item.scheduleReady===false).length;
  const errors=countOf(report.errors)+(unresolvedAuto>0?countOf(sync.errors):0);
  const syncPending=Boolean((sync.skippedBusy||sync.restartRequired||sync.waitingForXSchedule)&&unresolvedAuto>0);
  const hasProblem=incomplete>0||errors>0||syncPending||(added>0&&report.persisted===false);
  const addedIds=Array.isArray(report.addedShows)?report.addedShows:[];
  const addedNames=addedIds.map(id=>(items||[]).find(show=>show.id===id)?.title||id).filter(Boolean);

  importSummary.hidden=false;
  importSummary.classList.toggle('has-warning',hasProblem);
  importSummary.classList.toggle('has-new',added>0&&!hasProblem);
  importTitle.textContent=errors>0?'AUTOIMPORTACIÓN CON ERRORES':syncPending?'PENDIENTE EN XSCHEDULE':
    incomplete>0?'HAY CARPETAS INCOMPLETAS':added>0?'NUEVOS SHOWS INTERNOS':'CARPETAS VERIFICADAS';
  importState.textContent=errors>0?'ERROR':sync.skippedBusy?'PRÓXIMO INICIO':sync.restartRequired?'REINICIAR':
    incomplete>0?'REVISAR':added>0?'IMPORTADO':'SIN CAMBIOS';
  importAdded.textContent=String(added);
  importIncomplete.textContent=String(incomplete);
  importErrors.textContent=String(errors);
  const discoveryMessage=String(report.message||'').trim();
  const syncMessage=!syncPending&&(sync.skippedBusy||sync.restartRequired||sync.waitingForXSchedule)?
    'xSchedule ya confirmó las playlists autoimportadas.':
    sync.skippedBusy?'xSchedule está ocupado: se importará al próximo inicio.':
    sync.waitingForXSchedule?'Abre xSchedule y vuelve a iniciar V20 para importar de forma segura.':
    sync.restartRequired?'Reinicia xSchedule y V20 para confirmar las playlists nuevas.':
    sync.runtimeReloaded?'xSchedule actualizado y listo.':String(sync.message||'').trim();
  const addedMessage=addedNames.length?`Agregados: ${addedNames.join(', ')}.`:'';
  importMessage.textContent=[addedMessage,discoveryMessage,syncMessage].filter(Boolean).join(' ')||
    `${countOf(report.scannedFolders)} carpetas revisadas al iniciar V20.`;
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
  renderAutoImport(s.autoImport,s.scheduleSync,s.shows);
  renderShows(s.shows,xs.playlist,s.scheduleSync);
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
