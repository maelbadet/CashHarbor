// Native, accessible dialogs. Forms retain their original POST actions and CSRF fields.
const modalTargets = new Map();
let modalNumber = 0;
function createModal(content, title, trigger) {
  const dialog = document.createElement('dialog'); dialog.className = 'app-modal';
  const headingId = `modal-heading-${++modalNumber}`; dialog.setAttribute('aria-labelledby', headingId);
  const header = document.createElement('header'); header.className = 'modal-header';
  const heading = document.createElement('h2'); heading.id = headingId; heading.textContent = title;
  const close = document.createElement('button'); close.type = 'button'; close.className = 'modal-close'; close.textContent = '×'; close.setAttribute('aria-label', 'Fermer la fenêtre');
  header.append(heading, close); const body = document.createElement('div'); body.className = 'modal-body'; body.append(content);
  content.querySelector('input:not([type=hidden]),select,textarea')?.setAttribute('autofocus',''); dialog.append(header, body); document.body.append(dialog);
  let previousFocus;
  const open = () => { if(dialog.open) return; previousFocus = document.activeElement; dialog.showModal(); document.documentElement.classList.add('modal-open'); body.scrollTop=0; };
  const dismiss = () => dialog.close(); close.addEventListener('click', dismiss);
  dialog.addEventListener('click', event => { const rect=dialog.getBoundingClientRect(); if(event.target===dialog && (event.clientX<rect.left||event.clientX>rect.right||event.clientY<rect.top||event.clientY>rect.bottom)) dismiss(); });
  dialog.addEventListener('close', () => { if(!document.querySelector('dialog[open]'))document.documentElement.classList.remove('modal-open'); if(previousFocus?.isConnected)previousFocus.focus({preventScroll:true}); });
  trigger.addEventListener('click', open); trigger.setAttribute('aria-haspopup','dialog');
  for(const node of [content,...content.querySelectorAll('[id]')])if(node.id)modalTargets.set(node.id,open);
  return {dialog, open};
}
const creationActions = new Set(['/actions/account-add','/actions/transaction-add','/actions/goal-save','/actions/recurrence-add','/actions/loan-save','/portfolio/add','/budget/save']);
const processed = new Set();
for(const form of [...document.querySelectorAll('form[method="post"]')]){
  const action = new URL(form.action).pathname;
  if(!creationActions.has(action) || form.querySelector('input[name="id"]'))continue;
  const section=form.closest('section'); if(!section || processed.has(section) || section.classList.contains('account-card'))continue;
  processed.add(section);
  const title=section.querySelector('h2')?.textContent.trim() || (action==='/budget/save'?'Préparer mon budget':'Ajouter un élément');
  const launch=document.createElement('section'); launch.className='panel modal-launch';
  const text=document.createElement('div');const h=document.createElement('h2');h.textContent=title;const p=document.createElement('p');p.textContent=action==='/budget/save'?'Ajustez vos revenus prévus, plafonds et provisions dans une seule fenêtre.':'Saisissez les informations dans une fenêtre dédiée, sans quitter cette page.';text.append(h,p);
  const button=document.createElement('button');button.type='button';button.textContent=action==='/budget/save'?'Configurer ce mois':'＋ '+title;
  launch.append(text,button);section.replaceWith(launch);createModal(section,title,button);
  const pageHeader=document.querySelector('.main .page-header');
  const existingTrigger=section.id && pageHeader && [...pageHeader.querySelectorAll('a[href]')].some(link=>new URL(link.href).hash==='#'+section.id);
  if(existingTrigger)launch.classList.add('modal-launch-secondary');
  else if(pageHeader)pageHeader.insertAdjacentElement('afterend',launch);
}
for(const card of document.querySelectorAll('.account-card')){
  const actions=document.createElement('div');actions.className='account-quick-actions';
  for(const details of [...card.querySelectorAll(':scope > details')]){
    const summary=details.querySelector('summary');if(!summary)continue;
    const title=summary.textContent.trim();const content=document.createElement('section');content.className='panel';
    for(const child of [...details.childNodes])if(child!==summary)content.append(child);
    const button=document.createElement('button');button.type='button';button.className=details.classList.contains('danger-zone')?'secondary account-danger':'secondary';
    button.textContent=title.includes('Évolution')?'Historique et solde':title.includes('Modifier')?'Modifier':title.includes('Partager')?'Partager':'Supprimer';
    details.remove();actions.append(button);createModal(content,title+' · '+(card.querySelector('h2')?.textContent??''),button);
  }
  card.append(actions);
}
function openHash(hash){if(!hash)return false;let id;try{id=decodeURIComponent(hash.slice(1));}catch{return false;}const open=modalTargets.get(id);if(open){open();return true;}return false;}
document.addEventListener('click',event=>{const link=event.target.closest('a[href]');if(!link||event.ctrlKey||event.metaKey||event.shiftKey||event.altKey)return;const url=new URL(link.href);if(url.origin===location.origin&&url.pathname===location.pathname&&(url.search===location.search||url.searchParams.get('espace')===new URL(document.querySelector('.space-switch .selected')?.href??location.href).searchParams.get('espace'))&&openHash(url.hash)){event.preventDefault();}});
openHash(location.hash);
window.addEventListener('hashchange',()=>openHash(location.hash));
