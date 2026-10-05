import {useEffect,useState} from 'react';
import { Resolver, useForm, Controller } from 'react-hook-form';
import { getApiError, api } from '../services/Api';
import { useMyContext } from '../contexts/MyContext';
import { BiTask } from 'react-icons/bi';
import { format, parseISO, isBefore, isAfter } from 'date-fns';
import { toast, ToastContainer } from 'react-toastify';



interface FormData {
    name: string;
    description: string
    projectId: string
    startDate?: string | undefined
    endDate?: string | undefined
    collaboratorId?: string | null
}


const resolver: Resolver<FormData> = async (values) => {
    return {
        values: values.name ? values : {},
        errors: !values.name
            ? {
                name: {
                    type: 'required',
                    message: 'Este campo é obrigatório',
                },
            }
            : values.name.length > 200
                ? {
                    name: {
                        type: 'maxLength',
                        message: 'O nome do projeto não pode ter mais do que 200 caracteres',
                    },
                }
                : !values.projectId || values.projectId === "Escolha o Projeto"
                    ? {
                        projectId: {
                            type: 'required',
                            message: 'Este campo é obrigatório',
                        },
                    }
                    : {},
    };
};



interface Iprops {

    visible: boolean
    close: Function
}

export default function CreateTaskModal({ visible, close }: Iprops) {
    const { setError, register, handleSubmit, reset, formState: { errors } } = useForm<FormData>({ resolver });
    const [options,setOptions]=useState<any[]>([]);
    useEffect(()=>{if(visible)api.get("/project-options").then(({data})=>setOptions(data)).catch(e=>setError("projectId",{type:"server",message:getApiError(e)}));},[visible]);
    const { setProjects, projects, setTasks, setIsLoading, collaborators, setDayMinutes, setMonthMinutes, SuccessToast, ErrorToast } = useMyContext()


    async function createTask(data: FormData) {
        setIsLoading(true);
        try {
            await api.post('/tasks', data);
            const [tasks, projects, day, month] = await Promise.all([
                api.get('/tasks'), api.get('/projects'), api.post('/daytotalminutes', { daySent: new Date() }), api.get('/monthtotalminutes')]);
            setTasks(tasks.data); setProjects(projects.data); setDayMinutes(day.data); setMonthMinutes(month.data);
            close(); resetFields(); SuccessToast('Registro criado com sucesso!');
        } catch (error) {
            setError('endDate', { type: 'server', message: getApiError(error) });
        } finally { setIsLoading(false); }
    }
    function resetFields() {
        reset({ name: '', description: '', projectId: 'Escolha o Projeto', collaboratorId: 'Escolha o Colaborador', startDate: '', endDate: '' })
    }

    const onSubmit = (data: FormData) => {
        data.startDate = data.startDate ? new Date(String(data.startDate)).toISOString() : undefined;
        data.endDate = data.endDate ? new Date(data.endDate).toISOString() : undefined;
        if (data.collaboratorId === 'Escolha o Colaborador' || !data.collaboratorId) data.collaboratorId = null;
        
        return createTask(data);
    };
    return (
        <>
            <ToastContainer />
            <div className={`${!visible && 'hidden'} w-screen h-screen bg-black bg-opacity-80 backdrop:blur-3xl flex items-center justify-center z-50 absolute top-0 left-0`}>
                <div className="bg-slate-900 w-[90vw] md:w-[40vw] min-w-[350px] px-8 py-2 rounded-md justify-between flex flex-col">
                    <form
                        className='w-full items-center justify-center'
                        onSubmit={handleSubmit(onSubmit)}>
                        <div className='flex w-full justify-between pt-4'>
                            <BiTask size={28} />
                            <h1 className='mb-4 font-extrabold text-xl'>Criar Tarefa:</h1>

                        </div>

                        <label>
                            Nome:
                            <input
                                className="px-4 py-2 rounded bg-black w-full"
                                {...register("name")} placeholder="Nome da Tarefa" />
                            {errors?.name && <p className='text-red-700 text-center font-bold '>{errors.name.message}</p>}
                        </label>

                        <label>
                            Descrição:
                            <textarea
                                className="px-4 py-2 rounded bg-black w-full resize-none"
                                {...register("description")} placeholder="Descrição da Tarefa" />
                        </label>

                        <label>
                            Selecione um projeto:
                            <select
                                className="px-4 py-2 rounded bg-black w-full"
                                {...register("projectId")} >
                                <option >Escolha o Projeto</option>
                                {options.map((project: any) => (
                                    <option key={project.id} value={project.id}>
                                        {project.name}
                                    </option>
                                ))}

                            </select>
                            {errors?.projectId && <p className='text-red-700 text-center font-bold '>{errors.projectId.message}</p>}

                        </label>

                        <label>
                            Colaborador?
                            <select
                                className="px-4 py-2 rounded bg-black w-full"
                                {...register("collaboratorId")} >
                                <option >Escolha o Colaborador</option>
                                {collaborators?.map((colab) => (
                                    <option key={colab?.id} value={String(colab.id)}>
                                        {colab?.name}
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
                        <div className='flex items-center justify-center p-4 gap-2 mt-1'>
                            <button type='submit' className="bg-orange-600 px-4 w-22 flex justify-center rounded hover:opacity-80">
                                CRIAR
                            </button>
                            <button type="button"
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
        </>
    );
}
