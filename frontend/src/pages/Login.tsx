import { FormEvent, useContext, useEffect, useRef, useState } from 'react';
import { api, getApiError } from '../services/Api';
import { Navigate } from 'react-router-dom';
import Logo from '../components/Logo';
import { AuthContext } from '../contexts/AuthContext';
export default function Login() {
    const [signup, setSignup] = useState(false);
    const [name, setName] = useState('');
    const [organization, setOrganization] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const params=new URLSearchParams(window.location.search);
    const [recovery,setRecovery]=useState(params.get('action')==='reset');
    const [message,setMessage]=useState('');
    const [working,setWorking]=useState(false);
    const handled=useRef(false);
    useEffect(()=>{
        if(params.get('action')==='verify'&&!handled.current){handled.current=true;
            api.post('/verify-email',{token:params.get('token')}).then(({data})=>{setMessage(data.message);window.history.replaceState({},'', '/');}).catch(e=>setError(getApiError(e)));
        }
    },[]);
    const { signIn, signUp, authenticated, error, setError, activeButton } = useContext(AuthContext);
    if (authenticated) return <Navigate to="/home" replace />;
    async function submit(event: FormEvent) {
        event.preventDefault();
        if(recovery){setWorking(true);setError(null);try{
            const reset=params.get('action')==='reset';
            const {data}=await api.post(reset?'/reset-password':'/forgot-password',reset?{token:params.get('token'),password}:{email});
            setMessage(data.message);if(reset){window.history.replaceState({},'','/');setRecovery(false);setPassword('');}
        }catch(e){setError(getApiError(e));}finally{setWorking(false);}return;}
        if (signup) await signUp({ name, organization, email, password });
        else await signIn(email, password);
    }
    const input = 'px-4 py-2 rounded bg-slate-900 w-full';
    return <main className="flex flex-col md:flex-row min-h-screen bg-[#0C0B10] justify-evenly items-center gap-8 p-6">
        <div className="flex flex-col p-2 gap-3 max-w-xs">
            <span>Bem-vindo ao</span><Logo size="text-4xl" />
            <p className="text-sm">Organize projetos, distribua tarefas e acompanhe as horas do time.</p>
            <img className="max-w-[200px] self-center" src="/assets/img/tasks-icon.png" alt="" />
        </div>
        <form onSubmit={submit} className="flex flex-col p-8 gap-4 bg-gray-800 w-full max-w-sm rounded border-orange-600 border-2">
            <h1 className="text-orange-500 font-bold text-lg text-center">{recovery?'RECUPERAR ACESSO':signup ? 'CRIAR ORGANIZAÇÃO' : 'ENTRAR'}</h1>
            {signup && !recovery && <>
                <label>Seu nome<input required maxLength={120} autoComplete="name" className={input} value={name} onChange={e => setName(e.target.value)} /></label>
                <label>Organização<input required maxLength={120} autoComplete="organization" className={input} value={organization} onChange={e => setOrganization(e.target.value)} /></label>
            </>}
            {params.get('action')!=='reset'&&<label>E-mail<input required type="email" maxLength={254} autoComplete="username" className={input} value={email} onChange={e => setEmail(e.target.value)} /></label>}
            {(!recovery||params.get('action')==='reset')&&<label>{recovery?'Nova senha':'Senha'}<input required type="password" minLength={signup||recovery ? 12 : 1} maxLength={128} autoComplete={signup||recovery ? 'new-password' : 'current-password'} className={input} value={password} onChange={e => setPassword(e.target.value)} /></label>}
            {signup && <small>A senha deve ter pelo menos 12 caracteres. Você será o administrador da organização.</small>}
            {error && <p role="alert" className="text-orange-400 text-sm">{error}</p>}
            {message&&<p role="status" className="text-green-300 text-sm">{message}</p>}
            <button disabled={!activeButton||working} className="py-2 rounded bg-orange-600 font-bold disabled:opacity-60">{!activeButton||working ? 'AGUARDE…' : recovery?'CONFIRMAR':signup ? 'CRIAR CONTA' : 'ENTRAR'}</button>
            <button type="button" className="text-sm underline" disabled={!activeButton} onClick={() => { if(recovery){setRecovery(false);window.history.replaceState({},'','/');}else setSignup(!signup); setError(null); }}>{recovery?'Voltar ao login':signup ? 'Já tenho uma conta' : 'Criar uma organização'}</button>
            {!signup&&!recovery&&<button type="button" className="text-sm underline" onClick={()=>{setRecovery(true);setError(null);}}>Esqueci minha senha</button>}
        </form>
    </main>;
}
