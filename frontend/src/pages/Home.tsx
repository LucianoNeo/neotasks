import { useContext, useEffect, useState } from "react";
import { ToastContainer } from "react-toastify";
import Collaborators from "../components/Collaborators";
import Dashboard from "../components/Dashboard";
import Header from "../components/Header";
import Account from "../components/Account";
import Audit from "../components/Audit";
import List from "../components/List";
import Loading from "../components/Loading";
import Menu from "../components/Menu";
import Projects from "../components/Projects";
import Tasks from "../components/Tasks";
import { AuthContext } from "../contexts/AuthContext";
import { useMyContext } from "../contexts/MyContext";
import { api } from "../services/Api";



function Home() {
  const [close, setClose] = useState(false)
  const [page, setPage] = useState('dashboard')
  const [collectionPage,setCollectionPage]=useState(1),[collectionSearch,setCollectionSearch]=useState(''),[counts,setCounts]=useState<any>({projects:0,tasks:0,collaborators:0});
  const [totals,setTotals]=useState<any>({projects:0,tasks:0});
  useEffect(()=>{const update=(event:Event)=>{const d=(event as CustomEvent).detail;setTotals((prev:any)=>({...prev,[d.url.slice(1)]:d.total}));};window.addEventListener('neotasks:collection',update);return()=>window.removeEventListener('neotasks:collection',update);},[]);
  useEffect(()=>{setCollectionPage(1);setCollectionSearch('');sessionStorage.setItem('page:/projects','1');sessionStorage.setItem('page:/tasks','1');sessionStorage.setItem('q:/projects','');sessionStorage.setItem('q:/tasks','');sessionStorage.setItem('filterBy:/tasks','task');},[page]);
  useEffect(()=>{if(!['projects','tasks','list'].includes(page))return;const url=page==='projects'?'/projects':'/tasks';sessionStorage.setItem('page:'+url,String(collectionPage));sessionStorage.setItem('q:'+url,collectionSearch);const timer=setTimeout(()=>{api.get(url).then(({data})=>url==='/projects'?setProjects(data):setTasks(data)).catch(ErrorToast);},250);return()=>clearTimeout(timer);},[page,collectionPage,collectionSearch]);
  const { setProjects, projects, setTasks, tasks, setIsLoading, setCollaborators, dayMinutes, setDayMinutes, monthMinutes, setMonthMinutes, ErrorToast, SuccessToast } = useMyContext()
  const { user, token, signOut } = useContext(AuthContext)
  useEffect(()=>{if(page==='dashboard')api.get('/counts').then(({data})=>setCounts(data)).catch(ErrorToast);},[page,token]);

  useEffect(() => {
    let active = true;
    setIsLoading(true);
    setProjects([]); setTasks([]); setCollaborators([]); setDayMinutes(null); setMonthMinutes(null);
    Promise.all([api.get('/projects'), api.get('/tasks'), api.get('/collaborators'),
      api.post('/daytotalminutes', { daySent: new Date() }), api.get('/monthtotalminutes')])
      .then(([projects, tasks, collaborators, day, month]) => {
        if (!active) return;
        setProjects(projects.data); setTasks(tasks.data); setCollaborators(collaborators.data);
        setDayMinutes(day.data); setMonthMinutes(month.data);api.get('/counts').then(({data})=>setCounts(data)).catch(ErrorToast);
      }).catch(error => { if (active) ErrorToast(error); })
      .finally(() => { if (active) setIsLoading(false); });
    return () => { active = false; };
  }, [token]);


  return (
    <>
      <ToastContainer />
      <div className="overflow-x-hidden overflow-y-hidden">
        <Loading />
        <Header close={close} setClose={setClose} username={user} />
        <Menu open={close} setPage={setPage} setClose={setClose} close={close} />
        {['projects','tasks','list'].includes(page)&&<div className="flex gap-3 flex-wrap px-12 py-3 text-white"><label>Buscar<input className="bg-slate-900 rounded ml-2 px-2" value={collectionSearch} onChange={e=>{setCollectionSearch(e.target.value);setCollectionPage(1);}} /></label><button disabled={collectionPage===1} onClick={()=>setCollectionPage(collectionPage-1)}>Anterior</button><span>Página {collectionPage} · {totals[page==='projects'?'projects':'tasks']} registros</span><button disabled={collectionPage*20>=totals[page==='projects'?'projects':'tasks']} onClick={()=>setCollectionPage(collectionPage+1)}>Próxima</button></div>}
        {page == 'dashboard' && <Dashboard setPage={setPage} counts={counts} projects={projects} tasks={tasks} dayMinutes={dayMinutes} monthMinutes={monthMinutes} />}

        {/* @ts-ignore */}
        {page == 'projects' && <Projects projects={projects} />}
        {/* @ts-ignore */}
        {page == 'tasks' && <Tasks tasks={tasks} />}
        {page == 'collaborators' && <Collaborators />}
        {page == 'list' && <List />}
        {page === 'account' && <Account />}
        {page === 'audit' && <Audit />}
      </div>

    </>
  )

}

export default Home
