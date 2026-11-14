import React from 'react';
import FormInputGroup from 'Components/Form/FormInputGroup';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import useAddMovieRootFolderHint from './useAddMovieRootFolderHint';

interface AddMovieRootFolderInputProps {
  name?: string;
  value?: string;
  movieFolder?: string;
  customFolder?: string;
  isWindows?: boolean;
  onChange: (change: InputChanged) => void;
  includeNoChange?: boolean;
  includeNoChangeDisabled?: boolean;
  includeMissingValue?: boolean;
}

function AddMovieRootFolderInput({
  name = 'rootFolderPath',
  value,
  movieFolder,
  customFolder,
  isWindows,
  onChange,
  ...otherProps
}: AddMovieRootFolderInputProps) {
  const { displayedFolder, helpText } = useAddMovieRootFolderHint(
    value,
    movieFolder,
    customFolder
  );

  return (
    <FormInputGroup
      {...otherProps}
      type={inputTypes.ROOT_FOLDER_SELECT}
      name={name}
      value={value}
      valueOptions={{
        movieFolder: displayedFolder,
        isWindows,
      }}
      selectedValueOptions={{
        movieFolder: displayedFolder,
        isWindows,
      }}
      helpText={helpText}
      onChange={onChange}
    />
  );
}

export default AddMovieRootFolderInput;
