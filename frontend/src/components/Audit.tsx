import {useEffect,useState} from 'react';
import {api,getApiError} from '../services/Api';
export default function Audit(){
 const [page,setPage]=useState(1),[data,setData]=useState<any>({items:[],total:0}),[error,setError]=useState('');
 useEffect(()=>{api.get('/audit',{params:{page}}).then(({data})=>{setData(data);setError('');}).catch(e=>setError(getApiError(e)));},[page]);
 return <main className="text-white w-[85%] mx-auto p-6"><h1 className="text-2xl font-bold">AUDITORIA</h1><p role="alert">{error}</p><div className="overflow-auto mt-4"><table className="w-full text-left"><thead><tr><th>Data</th><th>Autor</th><th>Operação</th><th>Registro</th></tr></thead><tbody>{data.items.map((e:any)=><tr key={e.id}><td className="py-2">{new Date(e.at).toLocaleString('pt-BR')}</td><td>{e.actor==='anonymous'?'Cadastro inicial':e.actor}</td><td>{e.operation}</td><td>{e.resource}</td></tr>)}</tbody></table></div><div className="flex gap-4 mt-4"><button disabled={page===1} onClick={()=>setPage(page-1)}>Anterior</button><span>Página {page} · {data.total} alterações</span><button disabled={page*20>=data.total} onClick={()=>setPage(page+1)}>Próxima</button></div></main>;
}
