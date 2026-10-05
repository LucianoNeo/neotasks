import { useEffect, useMemo, useState } from 'react';
import ReactPaginate from 'react-paginate';
import { ClipLoader } from 'react-spinners';
import { useMyContext } from '../contexts/MyContext';
import { api } from '../services/Api';

type ReportTask = {
    id: string; name: string; project: { name: string };
    TimeTracker: { collaborator: { id: string; name: string } | null }[];
};
function Items({ currentItems }: { currentItems: ReportTask[] }) {
    const { ErrorToast } = useMyContext();
    const [times, setTimes] = useState<Record<string, string>>({});
    const [loading, setLoading] = useState(true);
    useEffect(() => {
        let active = true;
        setLoading(true);
        Promise.all(currentItems.map(async item => {
            const { data } = await api.get('/tasktotalminutes/' + item.id);
            const minutes = Math.floor(Number(data));
            const value = String(Math.floor(minutes / 60)).padStart(2, '0') + ':' + String(minutes % 60).padStart(2, '0');
            return [item.id, value] as const;
        })).then(values => { if (active) setTimes(Object.fromEntries(values)); })
          .catch(error => { if (active) { setTimes({}); ErrorToast(error); } })
          .finally(() => { if (active) setLoading(false); });
        return () => { active = false; };
    }, [currentItems]);
    return <div className="overflow-x-auto w-full text-center my-4">
        <table className="text-white bg-[#1E293B] text-xs md:text-sm w-full">
            <thead><tr>{['Tarefa', 'Projeto', 'Colaborador(es)', 'Tempo aportado'].map(title =>
                <th key={title} className="p-3 border" scope="col">{title}</th>)}</tr></thead>
            <tbody>{currentItems.map(item => {
                const names = [...new Map(item.TimeTracker.filter(entry => entry.collaborator)
                    .map(entry => [entry.collaborator!.id, entry.collaborator!.name])).values()];
                return <tr key={item.id} className="border">
                    <td className="p-3">{item.name}</td><td className="p-3">{item.project.name}</td>
                    <td className="p-3">{names.join(', ') || '—'}</td>
                    <td className="p-3">{loading ? <ClipLoader size={10} color="#e58b15" /> : times[item.id] || '—'}</td>
                </tr>;
            })}{!currentItems.length && <tr><td colSpan={4} className="p-4">Nenhuma tarefa encontrada.</td></tr>}</tbody>
        </table>
    </div>;
}
export default function PaginatedItems({ itemsPerPage, items }: { itemsPerPage: number; items: ReportTask[] }) {
    const { filter, filterBy } = useMyContext();
    const [itemOffset, setItemOffset] = useState(0);
    const filtered = useMemo(() => {
        const text = filter.toLocaleLowerCase('pt-BR');
        return items.filter(item => {
            if (filterBy === 'project') return item.project.name.toLocaleLowerCase('pt-BR').includes(text);
            if (filterBy === 'collaborator') return item.TimeTracker.some(entry => entry.collaborator?.name.toLocaleLowerCase('pt-BR').includes(text));
            return item.name.toLocaleLowerCase('pt-BR').includes(text);
        });
    }, [items, filter, filterBy]);
    useEffect(() => { setItemOffset(0); }, [filter, filterBy, items]);
    const currentItems = useMemo(() => filtered.slice(itemOffset, itemOffset + itemsPerPage), [filtered, itemOffset, itemsPerPage]);
    const pageCount = Math.ceil(filtered.length / itemsPerPage);
    return <><Items currentItems={currentItems} />{pageCount > 1 && <ReactPaginate
        breakLabel="…" nextLabel="Próximo >" previousLabel="< Anterior"
        className="text-xs text-center flex gap-2 m-auto md:text-sm"
        onPageChange={event => setItemOffset(event.selected * itemsPerPage)}
        forcePage={Math.floor(itemOffset / itemsPerPage)} pageRangeDisplayed={5} pageCount={pageCount} />}</>;
}
