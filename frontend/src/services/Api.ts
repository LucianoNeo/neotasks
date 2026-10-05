import axios from 'axios';
export const api = axios.create({ baseURL: '/app-api', timeout: 15000 });
export function getApiError(error: unknown): string {
    if (axios.isAxiosError(error)) {
        return error.response?.data?.error || error.response?.data?.detail ||
            (error.response?.status === 403 ? 'Você não tem permissão para esta operação.' :
             error.response?.status === 404 ? 'O registro não foi encontrado.' :
             error.response?.status === 401 ? 'Sua sessão expirou. Entre novamente.' :
             'Não foi possível concluir a operação. Verifique a conexão e tente novamente.');
    }
    return typeof error === 'string' ? error : 'Não foi possível concluir a operação.';
}
api.interceptors.request.use(config => {
    if(['/projects','/tasks'].includes(config.url||''))config.params={page:Number(sessionStorage.getItem('page:'+config.url)||1),q:sessionStorage.getItem('q:'+config.url)||'',filterBy:sessionStorage.getItem('filterBy:'+config.url)||'task',...config.params};
    const token = sessionStorage.getItem('neotasks:token');
    if (token) config.headers.Authorization = 'Bearer ' + token;
    const offsetMinutes = -new Date().getTimezoneOffset();
    if (config.url === '/daytotalminutes') config.data = { ...config.data, offsetMinutes };
    if (config.url === '/monthtotalminutes') config.params = { ...config.params, offsetMinutes };
    return config;
});
let renewal: Promise<string> | null = null;
api.interceptors.response.use(response => {
    if(['/projects','/tasks'].includes(response.config.url||''))window.dispatchEvent(new CustomEvent('neotasks:collection',{detail:{url:response.config.url,total:Number(response.headers['x-total-count']||0)}}));
    return response;
}, async error => {
    const config=error.config;
    if(error.response?.status===401 && !config?._retried && !['/login','/refresh','/register'].includes(config?.url)) {
        config._retried=true;
        try {
            if(!renewal)renewal=axios.post('/app-api/refresh').then(({data})=>{sessionStorage.setItem('neotasks:token',data.token);return data.token;}).finally(()=>{renewal=null;});
            const token=await renewal;config.headers.Authorization='Bearer '+token;return api.request(config);
        } catch {window.dispatchEvent(new Event('neotasks:session-expired'));}
    }
    return Promise.reject(error);
});
