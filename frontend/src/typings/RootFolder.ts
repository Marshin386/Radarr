import ModelBase from 'App/ModelBase';

interface RootFolder extends ModelBase {
  id: number;
  path: string;
  folderLayout: 'movieFolder' | 'flat' | 'custom';
  accessible: boolean;
  freeSpace?: number;
  unmappedFolders: object[];
}

export default RootFolder;
