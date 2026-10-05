import { FormEvent, useState } from 'react';
import { api, getApiError } from '../services/Api';
import { useMyContext } from '../contexts/MyContext';
export default function CreateMemberModal({ close }: { close: () => void }) {
    const [name, setName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const { setCollaborators } = useMyContext();
    async function submit(event: FormEvent) {
        event.preventDefault(); setSaving(true); setError('');
        try {
            await api.post('/collaborators', { name, email, password });
            setCollaborators((await api.get('/collaborators')).data); close();
        } catch (error) { setError(getApiError(error)); }
        finally { setSaving(false); }
    }
    return <div className="fixed inset-0 bg-black/80 flex items-center justify-center z-50 p-4">
        <form onSubmit={submit} aria-label="Novo colaborador" className="bg-slate-900 rounded p-6 w-full max-w-sm flex flex-col gap-4">
            <h2 className="font-bold text-xl">Novo colaborador</h2>
            <label>Nome<input required maxLength={120} className="bg-slate-800 p-2 w-full" value={name} onChange={e => setName(e.target.value)} /></label>
            <label>E-mail<input type="email" required maxLength={254} className="bg-slate-800 p-2 w-full" value={email} onChange={e => setEmail(e.target.value)} /></label>
            <label>Senha inicial<input type="password" autoComplete="new-password" required minLength={12} maxLength={128} className="bg-slate-800 p-2 w-full" value={password} onChange={e => setPassword(e.target.value)} /></label>
            <small>O colaborador poderá entrar com esse e-mail e senha. Ele terá acesso às tarefas da organização.</small>
            {error && <p role="alert" className="text-orange-400">{error}</p>}
            <button disabled={saving} className="bg-orange-600 p-2 rounded disabled:opacity-60">{saving ? 'Salvando…' : 'Adicionar colaborador'}</button>
            <button type="button" disabled={saving} onClick={close}>Cancelar</button>
        </form>
    </div>;
}
