import {useEffect,useState} from 'react';
import {api,getApiError} from '../services/Api';
export default function Account(){
 const [account,setAccount]=useState<any>(null),[message,setMessage]=useState('');
 useEffect(()=>{api.get('/account').then(({data})=>setAccount(data)).catch(e=>setMessage(getApiError(e)));},[]);
 async function resend(){try{const{data}=await api.post('/resend-verification');setMessage(data.message);}catch(e){setMessage(getApiError(e));}}
 return <main className="text-white w-[80%] mx-auto p-6"><h1 className="text-2xl font-bold">MINHA CONTA</h1>{account&&<section className="bg-slate-900 p-6 mt-4 rounded"><p>{account.name}</p><p>{account.email}</p><p className="my-4">{account.emailVerified?'E-mail confirmado':'E-mail ainda não confirmado'}</p>{!account.emailVerified&&<button className="bg-orange-600 p-2 rounded" onClick={resend}>Reenviar confirmação</button>}<p className="mt-4">Para mudar sua senha, saia da conta e use “Esqueci minha senha”.</p></section>}<p role="status">{message}</p></main>;
}
