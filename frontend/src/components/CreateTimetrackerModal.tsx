
import { useForm } from 'react-hook-form';
import { useMyContext } from '../contexts/MyContext';
import { getApiError, api } from '../services/Api';

import { format, parseISO, isBefore, isAfter } from 'date-fns';
import { toast } from 'react-toastify';



interface FormData {
    startDate?: string | undefined
    endDate?: string | undefined
    collaboratorId?: string | null
    taskId: string
}


interface Iprops {

    visible: boolean
    close: Function
    id: string
}

export default function CreateTimetrackerModal({ visible, close, id }: Iprops) {
    const { setError, register, handleSubmit, reset, formState: { errors } } = useForm<FormData>();
    const { setProjects, setTasks, setIsLoading, collaborators, setDayMinutes, setMonthMinutes, SuccessToast, ErrorToast } = useMyContext()


    async function createTimetracker(data: FormData) {
        setIsLoading(true);
        try {
            await api.post('/timetrackers', data);
            const [tasks, projects, day, month] = await Promise.all([
                api.get('/tasks'), api.get('/projects'), api.post('/daytotalminutes', { daySent: new Date() }), api.get('/monthtotalminutes')]);
            setTasks(tasks.data); setProjects(projects.data); setDayMinutes(day.data); setMonthMinutes(month.data);
            close(); resetFields(); SuccessToast('Registro criado com sucesso!');
        } catch (error) {
            setError('endDate', { type: 'server', message: getApiError(error) });
        } finally { setIsLoading(false); }
    }
    function resetFields() {
        reset({ collaboratorId: 'Escolha o Colaborador', startDate: '', endDate: '' })
    }


    const onSubmit = (data: FormData) => {
        data.startDate = data.startDate ? new Date(String(data.startDate)).toISOString() : undefined;
        data.endDate = data.endDate ? new Date(data.endDate).toISOString() : undefined;
        if (data.collaboratorId === 'Escolha o Colaborador' || !data.collaboratorId) data.collaboratorId = null;
        data.taskId = id;
        return createTimetracker(data);
    };
    return (
        <div className={`${!visible && 'hidden'} w-screen h-screen bg-black bg-opacity-80 backdrop:blur-3xl flex items-center justify-center z-50 absolute top-0 left-0`}>
            <div className="bg-slate-900 w-[90vw] md:w-[30vw] min-w-[350px] px-8 py-8 rounded-md justify-between flex flex-col text-left">
                <form
                    className='w-full items-center justify-center'
                    onSubmit={handleSubmit(onSubmit)}>
                    <h1 className='mb-4 text-lg font-extrabold' >Criar Timetracker:</h1>

                    <label>
                        Colaborador?
                        <select
                            className="px-4 py-2 rounded bg-black w-full"
                            {...register("collaboratorId")} >
                            <option >Escolha o Colaborador</option>
                            {collaborators!.map((colab) => (
                                <option key={colab.id} value={String(colab.id)}>
                                    {colab.name}
                                </option>
                            ))}

                        </select>
                    </label>

                    <label>
                        Início:
                        <input
                            type='datetime-local'
                            className="px-4 py-2 rounded bg-black w-full"
                            {...register("startDate")} />
                        {errors?.startDate && <p className='text-red-700 text-center font-bold '>{errors.startDate.message}</p>}
                    </label>
                    <label>
                        Fim:
                        <input
                            type='datetime-local'
                            className="px-4 py-2 rounded bg-black w-full"
                            {...register("endDate")} />
                        {errors?.endDate && <p className='text-red-700 text-center font-bold '>{errors.endDate.message}</p>}
                    </label>
                    <div className='flex items-center justify-center p-4 gap-2 mt-8'>
                        <button type='submit' className="bg-orange-600 px-4 w-22 flex justify-center rounded hover:opacity-80">
                            CRIAR
                        </button>
                        <button
                            type='button'
                            onClick={() => {
                                close()
                                resetFields()
                            }
                            }
                            className="bg-red-600 px-2 max-w-22 flex justify-center rounded hover:opacity-80">
                            CANCELAR
                        </button>

                    </div>
                </form>
            </div>
        </div>
    );
}
